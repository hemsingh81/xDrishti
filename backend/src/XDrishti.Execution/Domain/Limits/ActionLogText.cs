using System.Globalization;
using XDrishti.Domain.Execution.Limits;
using XDrishti.Domain.Market;

namespace XDrishti.Execution.Domain.Limits;

/// <summary>The kinds and actors of <c>execution.action_log</c> (spec P12-09 Data, criterion 11). The database checks the same lists.</summary>
public static class ActionKinds
{
    public const string AutoEntry = "auto_entry";
    public const string TapEntry = "tap_entry";
    public const string AutoRefused = "auto_refused";
    public const string TapRefused = "tap_refused";
    public const string AutoStopTighten = "auto_stop_tighten";
    public const string AutoExit = "auto_exit";
    public const string AutoCancel = "auto_cancel";
    public const string LossCapHalt = "loss_cap_halt";
    public const string LossCapNotice = "loss_cap_notice";

    public const string ByAuto = "auto";
    public const string ByTap = "tap";
    public const string BySystem = "system";
}

/// <summary>
/// The plain-language text of an action-log row (spec P12-09 criterion 11), culture-free. The text is for the owner's feed (P12-10); it is never logged. Money is shown whole when it has no paise, else with two decimals.
/// </summary>
public static class ActionLogText
{
    /// <summary>"Placed BUY 120 TATAPOWER at LIMIT 412.35 with stop 405.10 and target 425.80 because ticket T approved by you on 23 Oct was triggered at 09:46; day total Rs 49,482 of Rs 150,000, order 1 of 6".</summary>
    public static string Entry(AutoEntry entry, string ticket, bool byTap, decimal dayTotal, decimal dayCap, int order, int orderCap)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var approved = entry.ApprovalKind == "auto" ? "approved by a rule" : "approved by you";
        var on = entry.ApprovedAt is { } at ? " on " + IndiaTime.ToIst(at).ToString("d MMM", CultureInfo.InvariantCulture) : string.Empty;
        var why = byTap ? "you confirmed ticket " + ticket : "ticket " + ticket + " " + approved + on + " was triggered";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Placed {Side(entry.Side)} {entry.Quantity} {ExecutionLimits.TradingSymbol(entry.Symbol)} at LIMIT {Price(entry.LimitPrice)} with stop {Price(entry.Stop)} and target {Price(entry.Target)} because {why} at {IndiaTime.ToIst(entry.TriggeredAt):HH:mm}; day total Rs {Money(dayTotal)} of Rs {Money(dayCap)}, order {order} of {orderCap}");
    }

    /// <summary>"Did not place BUY 120 TATAPOWER for ticket T: cap.notional_day, feed.stale".</summary>
    public static string Refused(AutoEntry entry, string ticket, IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(codes);
        return string.Create(CultureInfo.InvariantCulture, $"Did not place {Side(entry.Side)} {entry.Quantity} {ExecutionLimits.TradingSymbol(entry.Symbol)} for ticket {ticket}: {string.Join(", ", codes)}");
    }

    public static string Money(decimal value) => value == decimal.Truncate(value) ? value.ToString("#,0", CultureInfo.InvariantCulture) : value.ToString("#,0.00", CultureInfo.InvariantCulture);

    public static string Price(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string Side(ExecSide side) => side == ExecSide.Buy ? "BUY" : "SELL";
}
