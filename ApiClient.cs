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

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

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
                d.Error = $"HTTP {(int)resp.StatusCode}";
            d.Unauthorized = (int)resp.StatusCode == 401;
            return d;
        }
        catch (Exception e)
        {
            return new UsageData { Error = e.Message };
        }
    }

    private static string ReadToken()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CredsPath));
        return doc.RootElement.GetProperty("claudeAiOauth").GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("accessToken missing in .credentials.json");
    }

    private static double H(HttpResponseMessage r, string name)
    {
        string? v = S(r, name);
        return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : 0.0;
    }

    private static string? S(HttpResponseMessage r, string name)
        => r.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;
}
