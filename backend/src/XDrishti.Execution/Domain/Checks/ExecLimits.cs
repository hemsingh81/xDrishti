using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Execution.Domain.Checks;

/// <summary>
/// The numbers the checks judge against (spec P12-03). Settings only move a limit inside the compiled range of <see cref="ExecSettingRegistry"/>; there is no setting that turns a check off. <see cref="FromValues"/> returns null when any
/// value in force is out of range, which the caller treats as "limits unavailable" (every run is then rejected).
/// </summary>
public sealed record ExecLimits(
    double PriceBandPct,
    double MaxOrderQty,
    double MaxOrderValueInr,
    double FeedMaxAgeS,
    double TokenMinRemainingMin,
    TimeOnly EntryWindowStart,
    TimeOnly EntryWindowEnd,
    double MaxLossPerTradeInr,
    double MaxSpreadTicks,
    double MaxSpreadPct,
    double MinDepthMultiple,
    double MaxOrdersPerDay,
    double CheckValidForS)
{
    // Soft limits are compiled (P7-05 numbers): they are not settings.
    public const double MaxAdvPct = 1.0;
    public const int ResultsWithinDays = 3;
    public const double MaxCorrelationAllowed = 0.70;

    public static ExecLimits Default { get; } = FromValues(new Dictionary<string, string>())!;

    public string Hash
    {
        get
        {
            var text = string.Join('|', PriceBandPct, MaxOrderQty, MaxOrderValueInr, FeedMaxAgeS, TokenMinRemainingMin, EntryWindowStart, EntryWindowEnd, MaxLossPerTradeInr, MaxSpreadTicks, MaxSpreadPct, MinDepthMultiple, MaxOrdersPerDay, CheckValidForS);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        }
    }

    /// <summary>Reads the settings in force; an absent key takes the registry default, a present key must be valid, else null.</summary>
    public static ExecLimits? FromValues(IReadOnlyDictionary<string, string> values)
    {
        if (Number(values, "exec.price_band_pct") is not { } band
            || Number(values, "exec.max_order_qty") is not { } qty
            || Number(values, "exec.max_order_value_inr") is not { } value
            || Number(values, "exec.feed_max_age_s") is not { } feed
            || Number(values, "exec.token_min_remaining_min") is not { } token
            || Time(values, "exec.entry_window_start") is not { } start
            || Time(values, "exec.entry_window_end") is not { } end
            || Number(values, "exec.max_loss_per_trade_inr") is not { } loss
            || Number(values, "exec.max_spread_ticks") is not { } ticks
            || Number(values, "exec.max_spread_pct") is not { } spreadPct
            || Number(values, "exec.min_depth_multiple") is not { } depth
            || Number(values, "exec.max_orders_per_day") is not { } orders
            || Number(values, "exec.check_valid_for_s") is not { } valid
            || start >= end)
        {
            return null;
        }

        return new ExecLimits(band, qty, value, feed, token, start, end, loss, ticks, spreadPct, depth, orders, valid);
    }

    private static string? Raw(IReadOnlyDictionary<string, string> values, string key)
    {
        var setting = ExecSettingRegistry.Find(key);
        if (setting is null)
        {
            return null;
        }

        var raw = values.TryGetValue(key, out var v) ? v : setting.Default;
        return ExecSettingRegistry.IsValid(setting, raw) ? raw : null;
    }

    private static double? Number(IReadOnlyDictionary<string, string> values, string key) =>
        Raw(values, key) is { } raw ? double.Parse(raw, CultureInfo.InvariantCulture) : null;

    private static TimeOnly? Time(IReadOnlyDictionary<string, string> values, string key) =>
        Raw(values, key) is { } raw ? TimeOnly.ParseExact(raw, "HH:mm", CultureInfo.InvariantCulture) : null;
}
