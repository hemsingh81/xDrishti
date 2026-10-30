using System.Globalization;

namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The three settings of the confirm ticket (spec P12-08 Data; each makes the system riskier when raised) and the feed age the drift guard trusts. The execution service's registry carries the same keys, defaults and ranges (a test pins the
/// agreement); the API reads them from the newest settings snapshot through <see cref="FromValues"/>, which keeps a value only when it parses and lies in range, else the default.
/// </summary>
public sealed record ConfirmSettings(int TtlSeconds, decimal MaxDriftPct, decimal StepUpNotionalPct, int FeedMaxAgeSeconds)
{
    public const string TtlKey = "exec.confirm_ttl_s";
    public const string DriftKey = "exec.confirm_max_drift_pct";
    public const string StepUpKey = "exec.confirm_step_up_notional_pct";
    public const string FeedMaxAgeKey = "exec.feed_max_age_s";

    public static readonly ConfirmSettings Default = new(90, 0.30m, 10m, 5);

    public static readonly (decimal Min, decimal Max) TtlRange = (15, 300);
    public static readonly (decimal Min, decimal Max) DriftRange = (0.05m, 1.00m);
    public static readonly (decimal Min, decimal Max) StepUpRange = (1, 100);
    public static readonly (decimal Min, decimal Max) FeedMaxAgeRange = (1, 30);

    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);

    public TimeSpan FeedMaxAge => TimeSpan.FromSeconds(FeedMaxAgeSeconds);

    public static ConfirmSettings FromValues(IReadOnlyDictionary<string, string>? values) => new(
        (int)Read(values, TtlKey, TtlRange, Default.TtlSeconds),
        Read(values, DriftKey, DriftRange, Default.MaxDriftPct),
        Read(values, StepUpKey, StepUpRange, Default.StepUpNotionalPct),
        (int)Read(values, FeedMaxAgeKey, FeedMaxAgeRange, Default.FeedMaxAgeSeconds));

    private static decimal Read(IReadOnlyDictionary<string, string>? values, string key, (decimal Min, decimal Max) range, decimal fallback) =>
        values is not null && values.TryGetValue(key, out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= range.Min && v <= range.Max
            ? v
            : fallback;
}
