using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// User-configurable settings, persisted as JSON in %LocalAppData%\ClaudeTray\settings.json.
/// Deliberately small for now — more fields will land here over time and the Settings dialog
/// grows with them. Any missing, corrupt, or out-of-range value falls back to its default, so
/// a hand-edited or older file can never crash the app.
/// </summary>
internal sealed class Settings
{
    public const int MinRefreshSeconds = 30;     // don't hammer the API
    public const int MaxRefreshSeconds = 3600;   // an hour between polls is plenty
    public const int DefaultRefreshSeconds = 300; // 5 min — the historical hardcoded cadence

    public const int MinAuthRetrySeconds = 5;       // floor on the signed-out re-check cadence
    public const int MaxAuthRetrySeconds = 300;     // 5 min is as slow as a retry needs to be
    public const int DefaultAuthRetrySeconds = 10;  // re-check every 10s while signed out

    public const string DefaultMetric = "5h";
    private static readonly string[] ValidMetrics = { "5h", "7d", "extra" };

    /// <summary>How often the tray polls the usage API, in seconds.</summary>
    public int RefreshSeconds { get; set; } = DefaultRefreshSeconds;

    /// <summary>Draw the usage percentage number on the tray icon (otherwise just the fill bar).</summary>
    public bool ShowPercentage { get; set; } = true;

    /// <summary>
    /// Show a tray notification on an unexpected drop in weekly usage — the counter resetting to 0%
    /// before its scheduled deadline, or a partial mid-window credit (e.g. 91% → 50%). Both are known
    /// Claude Code anomalies. Rare by nature, so enabled by default; turn it off to silence the alert.
    /// </summary>
    public bool NotifyOnUnexpectedReset { get; set; } = true;

    /// <summary>
    /// Show a (calmer) tray notification on the routine weekly reset too — the "fresh week, quota's
    /// back" ping. Off by default since it recurs every week; turn it on if you like the heads-up.
    /// </summary>
    public bool NotifyOnScheduledReset { get; set; } = false;

    /// <summary>Which usage window the tray displays: "5h", "7d", or "extra".</summary>
    public string Metric { get; set; } = DefaultMetric;

    /// <summary>The user's home directory — the default working directory when none is chosen.</summary>
    public static string DefaultDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Working directory Claude Code opens in — for the "Open Claude Code" menu item, a tray
    /// double-click, and auto-open. Never empty: an unset or cleared value falls back to the
    /// user's home directory (<see cref="DefaultDirectory"/>).
    /// </summary>
    public string ClaudeCodeDirectory { get; set; } = DefaultDirectory;

    /// <summary>
    /// When a poll returns HTTP 401 (expired token), automatically launch Claude Code so it can
    /// refresh the OAuth token without the user having to act.
    /// </summary>
    public bool AutoOpenOnUnauthenticated { get; set; } = false;

    /// <summary>While signed out, how often to re-poll the usage API (seconds) until auth returns.</summary>
    public int AuthRetrySeconds { get; set; } = DefaultAuthRetrySeconds;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeTray", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) is { } s)
            {
                s.Clamp();
                return s;
            }
        }
        catch { /* corrupt or unreadable — fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Clamp();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Clamp()
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        AuthRetrySeconds = Math.Clamp(AuthRetrySeconds, MinAuthRetrySeconds, MaxAuthRetrySeconds);
        if (string.IsNullOrWhiteSpace(ClaudeCodeDirectory))
            ClaudeCodeDirectory = DefaultDirectory;
        if (Array.IndexOf(ValidMetrics, Metric) < 0)
            Metric = DefaultMetric;
    }
}
