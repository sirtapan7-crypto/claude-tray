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
    // A reset counts as "unexpected" only when the previously-reported deadline was still this
    // far in the future — comfortably beyond the slowest poll cadence (1h), so an on-time reset
    // (where the deadline ≈ now) never trips it, but the days-early anomaly does.
    private const double UnexpectedResetMarginSeconds = 2 * 3600;

    private readonly Dictionary<string, Series> _series = new();

    /// <summary>A usage window that reset earlier than its previously-reported deadline — the
    /// signal behind the "unexpected reset" tray notification. Carries before/after numbers so
    /// the event can be reported with concrete values.</summary>
    internal readonly record struct UnexpectedReset(double PrevUtil, double PrevReset, double NewReset);

    /// <summary>
    /// Record a fresh utilization sample, auto-clearing history when the window resets. Returns a
    /// non-null <see cref="UnexpectedReset"/> when this sample shows the window resetting before its
    /// previously-reported deadline (usage fell to ~0% while the old reset time was still well in
    /// the future) — otherwise null.
    /// </summary>
    public UnexpectedReset? Record(string key, double util, double reset, double now)
    {
        if (!_series.TryGetValue(key, out Series? s))
            _series[key] = s = new Series();

        // A new window started if the reset timestamp jumped, or utilization fell sharply
        // (usage only climbs within a window). Either way, the old trend no longer applies.
        bool resetMoved = reset > 0 && s.LastReset > 0 && Math.Abs(reset - s.LastReset) > 1;
        double prevUtil = s.Samples.Count > 0 ? s.Samples[^1].u : 0;
        bool dropped = s.Samples.Count > 0 && util < prevUtil - 0.05;

        // The window reset before it was due: usage collapsed to ~0% while the deadline we last
        // saw was still comfortably in the future. A normal reset lands AT the deadline, where
        // s.LastReset - now ≈ 0, so it stays below the margin and is treated as routine.
        UnexpectedReset? unexpected =
            dropped && util < 0.05 && s.LastReset > 0 && s.LastReset - now > UnexpectedResetMarginSeconds
                ? new UnexpectedReset(prevUtil, s.LastReset, reset)
                : null;

        if (resetMoved || dropped)
            s.Samples.Clear();

        s.LastReset = reset;
        s.Samples.Add((now, util));
        if (s.Samples.Count > MaxSamples)
            s.Samples.RemoveAt(0);

        return unexpected;
    }

    /// <summary>
    /// Project the named metric: verdict, seconds until usage would hit 100% at the current
    /// pace, and the burn rate as a fraction of the limit per hour (for display).
    ///
    /// When <paramref name="windowSeconds"/> &gt; 0 the verdict is decided by the "pace line"
    /// instead of the regression: utilization is compared against the linear ideal consumption
    /// since the window started (elapsed = window − timeToReset). This is robust to short
    /// bursts because it averages over the whole elapsed window rather than the last ~2h. The
    /// ETA / burn rate are still derived from the regression for display.
    /// </summary>
    public (Projection verdict, double exhaustSeconds, double burnPerHour) Project(
        string key, double util, double reset, double now, double windowSeconds = 0)
    {
        // Regression-based burn rate and ETA (drives the verdict unless a pace-line window
        // is supplied below).
        Projection verdict = Projection.Unknown;
        double exhaust = 0, burnPerHour = 0;

        if (_series.TryGetValue(key, out Series? s) && s.Samples.Count >= 2 &&
            s.Samples[^1].t - s.Samples[0].t >= MinSpanSeconds)
        {
            // Least-squares slope (utilization per second), x re-based to the first sample.
            double n = s.Samples.Count, t0 = s.Samples[0].t;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach ((double t, double u) in s.Samples)
            {
                double x = t - t0;
                sx += x; sy += u; sxx += x * x; sxy += x * u;
            }
            double denom = n * sxx - sx * sx;
            if (denom > 0)
            {
                double burnPerSec = (n * sxy - sx * sy) / denom;
                if (burnPerSec <= 1e-9)
                {
                    // Flat or declining usage: nothing will be exhausted on its own.
                    verdict = Projection.Ok;
                    exhaust = double.PositiveInfinity;
                }
                else
                {
                    double remaining = Math.Max(0.0, 1.0 - util);
                    exhaust = remaining / burnPerSec;          // seconds until 100%
                    double untilReset = reset > 0 ? reset - now : double.PositiveInfinity;
                    verdict = exhaust < untilReset ? Projection.Danger : Projection.Ok;
                    burnPerHour = burnPerSec * 3600;
                }
            }
        }

        // Pace-line verdict for windows of known length: danger if we are already above the
        // share of the budget that even, constant consumption would have used by now. The ETA
        // is the rule-of-three projection from the average pace since the window started:
        // util reached in `elapsed` → 100% reached in elapsed × 100/util, so the time left to
        // hit 100% is elapsed × (1 − util) / util.
        if (windowSeconds > 0 && reset > 0)
        {
            double untilReset = reset - now;
            double elapsed = Math.Max(0.0, windowSeconds - untilReset);
            double expected = Math.Clamp(elapsed / windowSeconds, 0.0, 1.0);
            verdict = util > expected ? Projection.Danger : Projection.Ok;

            if (util >= 1.0)
                exhaust = 0;
            else if (util > 0)
                exhaust = elapsed * (1.0 - util) / util;
            else
                exhaust = double.PositiveInfinity;
        }

        return (verdict, exhaust, burnPerHour);
    }
}
