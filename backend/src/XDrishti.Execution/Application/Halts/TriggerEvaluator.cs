namespace XDrishti.Execution.Application.Halts;

/// <summary>What a trigger check came to. <see cref="Unavailable"/> (a missing, non-finite or negative input) never fires a halt and never counts as a good state that clears one.</summary>
public enum TriggerVerdict
{
    Clear,
    Fire,
    Unavailable,
}

/// <summary>
/// The pure threshold checks of P12-05 (criterion 5). Each returns <see cref="TriggerVerdict.Fire"/> at the boundary and beyond; the value just inside is <see cref="TriggerVerdict.Clear"/>. The numbers come from the settings in force; whoever calls
/// decides what Unavailable means (the pre-trade check treats it as a reject, the evaluator never fires on it).
/// </summary>
public static class TriggerEvaluator
{
    /// <summary>The day result (realised plus unrealised) at or below minus the limit, where the limit is min(r x risk budget, pct x capital) of P12-03.</summary>
    public static TriggerVerdict DailyLoss(decimal? dayResult, decimal? limit) =>
        dayResult is not { } result || limit is not { } l || l <= 0m ? TriggerVerdict.Unavailable : result <= -l ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>The result curve's peak less its value now, at least <paramref name="pct"/> percent of capital.</summary>
    public static TriggerVerdict Drawdown(decimal? peak, decimal? now, decimal? capital, decimal pct) =>
        peak is not { } p || now is not { } n || capital is not { } c || c <= 0m || p < n ? TriggerVerdict.Unavailable : p - n >= c * pct / 100m ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>The day result at or below minus <paramref name="pct"/> percent of capital.</summary>
    public static TriggerVerdict DisasterLoss(decimal? dayResult, decimal? capital, decimal pct) =>
        dayResult is not { } r || capital is not { } c || c <= 0m ? TriggerVerdict.Unavailable : r <= -(c * pct / 100m) ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>No fresh price for the limit while a position or a working entry exists during the session. No exposure or outside the session: nothing to protect, Clear.</summary>
    public static TriggerVerdict FeedLoss(double? secondsSinceFreshPrice, int limitSeconds, bool exposure, bool inSession) =>
        !inSession || !exposure ? TriggerVerdict.Clear : secondsSinceFreshPrice is not { } s || double.IsNaN(s) || s < 0 ? TriggerVerdict.Unavailable : s >= limitSeconds ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>More than the limit of sends in 10 seconds, or an attempted send above the limiter.</summary>
    public static TriggerVerdict Runaway(int? sendsInTenSeconds, int limit, bool limiterExceeded) =>
        limiterExceeded ? TriggerVerdict.Fire : sendsInTenSeconds is not { } n || n < 0 ? TriggerVerdict.Unavailable : n > limit ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>At least the limit of broker rejects in five minutes.</summary>
    public static TriggerVerdict RejectBurst(int? rejectsInFiveMinutes, int limit) =>
        rejectsInFiveMinutes is not { } n || n < 0 ? TriggerVerdict.Unavailable : n >= limit ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>At least the limit of starts in ten minutes.</summary>
    public static TriggerVerdict CrashLoop(int? startsInTenMinutes, int limit) =>
        startsInTenMinutes is not { } n || n < 0 ? TriggerVerdict.Unavailable : n >= limit ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>The newest reads, newest last: fires when the last three all exceed the limit (absolute milliseconds); fewer than three reads is Unavailable.</summary>
    public static TriggerVerdict ClockSkew(IReadOnlyList<double> recentSkewMs, int limitMs, int consecutive = 3)
    {
        ArgumentNullException.ThrowIfNull(recentSkewMs);
        if (recentSkewMs.Count < consecutive || recentSkewMs.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
        {
            return TriggerVerdict.Unavailable;
        }

        return recentSkewMs.TakeLast(consecutive).All(v => Math.Abs(v) > limitMs) ? TriggerVerdict.Fire : TriggerVerdict.Clear;
    }

    /// <summary>The Nifty 50 index at or beyond the percentage from the previous close, in either direction. Secondary sources only (UNVERIFIED).</summary>
    public static TriggerVerdict ExchangeHalt(decimal? previousClose, decimal? last, decimal pct) =>
        previousClose is not { } p || last is not { } l || p <= 0m || l <= 0m ? TriggerVerdict.Unavailable : Math.Abs(l - p) / p * 100m >= pct ? TriggerVerdict.Fire : TriggerVerdict.Clear;

    /// <summary>Broker calls failing at the connection level for the limit with an open position or working order.</summary>
    public static TriggerVerdict BrokerOutage(double? failingSeconds, int limitSeconds, bool exposure) =>
        !exposure ? TriggerVerdict.Clear : failingSeconds is not { } s || double.IsNaN(s) || s < 0 ? TriggerVerdict.Unavailable : s >= limitSeconds ? TriggerVerdict.Fire : TriggerVerdict.Clear;
}
