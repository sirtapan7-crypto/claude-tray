using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>Usage snapshot read from the Anthropic rate-limit response headers.</summary>
internal sealed class UsageData
{
    public double Session5h;
    public double Week7d;
    public double Extra;
    public double Reset5h;     // unix seconds
    public double Reset7d;
    public double ResetExtra;
    public string Status = "unknown";
    public string? Error;
    public bool Unauthorized;   // true on HTTP 401 — token expired, needs Claude Code to re-auth
    public bool Transient;      // true when Error is a timeout/network/5xx blip worth retrying quietly

    public double Metric(string key) => key switch
    {
        "7d" => Week7d,
        "extra" => Extra,
        _ => Session5h,
    };

    public double ResetOf(string key) => key switch
    {
        "7d" => Reset7d,
        "extra" => ResetExtra,
        _ => Reset5h,
    };
}

/// <summary>
/// Fetches usage via one minimal Anthropic API call (Haiku, 1 token) using the OAuth
/// token Claude Code stores in ~/.claude/.credentials.json, and reads the official
/// anthropic-ratelimit-unified-* headers from the response.
/// </summary>
internal sealed class ApiClient
{
    private static readonly string CredsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<UsageData> FetchAsync()
    {
        try
        {
            string token = ReadToken();

            var body = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "hi" } },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using HttpResponseMessage resp = await _http.SendAsync(req).ConfigureAwait(false);

            var d = new UsageData
            {
                Session5h = H(resp, "anthropic-ratelimit-unified-5h-utilization"),
                Week7d    = H(resp, "anthropic-ratelimit-unified-7d-utilization"),
                Extra     = H(resp, "anthropic-ratelimit-unified-overage-utilization"),
                Reset5h   = H(resp, "anthropic-ratelimit-unified-5h-reset"),
                Reset7d   = H(resp, "anthropic-ratelimit-unified-7d-reset"),
                ResetExtra = H(resp, "anthropic-ratelimit-unified-overage-reset"),
                Status    = S(resp, "anthropic-ratelimit-unified-5h-status") ?? "unknown",
            };

            // If the call itself failed (e.g. expired token) and no headers came back, surface it.
            if (!resp.IsSuccessStatusCode && d.Session5h == 0 && d.Week7d == 0)
            {
                int code = (int)resp.StatusCode;
                d.Transient = code >= 500 || code == 429;
                d.Error = d.Transient
                    ? $"Anthropic is temporarily unavailable (HTTP {code}). Retrying…"
                    : $"HTTP {code}";
            }
            d.Unauthorized = (int)resp.StatusCode == 401;
            return d;
        }
        catch (NotAuthenticatedException e)
        {
            // No usable token on disk yet (file missing, or no claudeAiOauth.accessToken). Treat it
            // like an HTTP 401 so the tray shows the "needs auth" state and the faster retry/auto-open
            // kicks in — but with a message that says to sign in, not a raw file-not-found error.
            return new UsageData { Error = e.Message, Unauthorized = true };
        }
        catch (Exception e)
        {
            return new UsageData { Error = Friendly(e), Transient = IsTransient(e) };
        }
    }

    // Timeouts (HttpClient throws TaskCanceledException), DNS/connection failures and similar
    // network blips are worth swallowing for a poll or two before alarming the user.
    private static bool IsTransient(Exception e) =>
        e is TaskCanceledException or OperationCanceledException or HttpRequestException
        || e.InnerException is HttpRequestException or System.Net.Sockets.SocketException;

    // Replace raw exception text (e.g. "The request was canceled due to the configured
    // HttpClient.Timeout of 30 seconds elapsing") with something a non-developer can read.
    private static string Friendly(Exception e) => e switch
    {
        TaskCanceledException or OperationCanceledException => "Couldn't reach Anthropic — the request timed out. Retrying…",
        HttpRequestException => "Couldn't reach Anthropic — network error. Retrying…",
        _ => e.Message,
    };

    // Shown verbatim in the tray tooltip / Insights when there's no token to use yet.
    private const string SignInHint =
        "Not signed in to Claude Code. Open Claude Code and run /login to sign in.";

    private static string ReadToken()
    {
        if (!File.Exists(CredsPath))
            throw new NotAuthenticatedException(SignInHint);

        using var doc = JsonDocument.Parse(File.ReadAllText(CredsPath));
        if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
            && oauth.TryGetProperty("accessToken", out var tok)
            && tok.GetString() is { Length: > 0 } token)
            return token;

        throw new NotAuthenticatedException(SignInHint);
    }

    /// <summary>No usable OAuth token on disk yet — surfaced as a "needs sign-in" state, not an error.</summary>
    private sealed class NotAuthenticatedException(string message) : Exception(message);

    private static double H(HttpResponseMessage r, string name)
    {
        string? v = S(r, name);
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : 0.0;
    }

    private static string? S(HttpResponseMessage r, string name)
        => r.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;
}
