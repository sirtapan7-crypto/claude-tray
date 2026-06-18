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

    public const string DefaultMetric = "5h";
    private static readonly string[] ValidMetrics = { "5h", "7d", "extra" };

    /// <summary>How often the tray polls the usage API, in seconds.</summary>
    public int RefreshSeconds { get; set; } = DefaultRefreshSeconds;

    /// <summary>Draw the usage percentage number on the tray icon (otherwise just the fill bar).</summary>
    public bool ShowPercentage { get; set; } = true;

    /// <summary>Which usage window the tray displays: "5h", "7d", or "extra".</summary>
    public string Metric { get; set; } = DefaultMetric;

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
        if (Array.IndexOf(ValidMetrics, Metric) < 0)
            Metric = DefaultMetric;
    }
}
