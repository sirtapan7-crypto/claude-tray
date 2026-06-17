namespace ClaudeTray;

/// <summary>Result of projecting the current usage trend against its reset deadline.</summary>
internal enum Projection
{
    Unknown, // not enough samples yet to estimate a burn rate
    Ok,      // at the current pace, usage stays under 100% until the window resets
    Danger,  // at the current pace, usage hits 100% before the window resets
}

/// <summary>
/// Observability for token usage: keeps a short rolling history of utilization samples
/// per metric (5h / 7d / extra) and estimates the burn rate (utilization per second) by
/// least-squares regression. From that it projects whether usage will reach 100% before
/// the window resets — the signal behind the tray's green/red dot.
/// </summary>
internal sealed class BurnTracker
{
    private sealed class Series
    {
        public readonly List<(double t, double u)> Samples = new();
        public double LastReset;
    }

    // ~2h of history at the 5-min poll cadence — recent enough to reflect the current pace.
    private const int MaxSamples = 24;
    // Need at least this much elapsed time between first and last sample to trust a slope.
    private const double MinSpanSeconds = 120;

    private readonly Dictionary<string, Series> _series = new();

    /// <summary>Record a fresh utilization sample, auto-clearing history when the window resets.</summary>
    public void Record(string key, double util, double reset, double now)
    {
        if (!_series.TryGetValue(key, out Series? s))
            _series[key] = s = new Series();

        // A new window started if the reset timestamp jumped, or utilization fell sharply
        // (usage only climbs within a window). Either way, the old trend no longer applies.
        bool resetMoved = reset > 0 && s.LastReset > 0 && Math.Abs(reset - s.LastReset) > 1;
        bool dropped = s.Samples.Count > 0 && util < s.Samples[^1].u - 0.05;
        if (resetMoved || dropped)
            s.Samples.Clear();

        s.LastReset = reset;
        s.Samples.Add((now, util));
        if (s.Samples.Count > MaxSamples)
            s.Samples.RemoveAt(0);
    }

    /// <summary>
    /// Project the named metric: verdict, seconds until usage would hit 100% at the current
    /// pace, and the burn rate as a fraction of the limit per hour (for display).
    /// </summary>
    public (Projection verdict, double exhaustSeconds, double burnPerHour) Project(
        string key, double util, double reset, double now)
    {
        if (!_series.TryGetValue(key, out Series? s) || s.Samples.Count < 2)
            return (Projection.Unknown, 0, 0);

        double span = s.Samples[^1].t - s.Samples[0].t;
        if (span < MinSpanSeconds)
            return (Projection.Unknown, 0, 0);

        // Least-squares slope (utilization per second), x re-based to the first sample.
        double n = s.Samples.Count, t0 = s.Samples[0].t;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach ((double t, double u) in s.Samples)
        {
            double x = t - t0;
            sx += x; sy += u; sxx += x * x; sxy += x * u;
        }
        double denom = n * sxx - sx * sx;
        if (denom <= 0)
            return (Projection.Unknown, 0, 0);

        double burnPerSec = (n * sxy - sx * sy) / denom;

        // Flat or declining usage: nothing will be exhausted on its own.
        if (burnPerSec <= 1e-9)
            return (Projection.Ok, double.PositiveInfinity, 0);

        double remaining = Math.Max(0.0, 1.0 - util);
        double exhaust = remaining / burnPerSec;               // seconds until 100%
        double untilReset = reset > 0 ? reset - now : double.PositiveInfinity;

        Projection verdict = exhaust < untilReset ? Projection.Danger : Projection.Ok;
        return (verdict, exhaust, burnPerSec * 3600);
    }
}
