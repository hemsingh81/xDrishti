using XDrishti.Domain.Execution.Limits;
using XDrishti.Domain.Market;

namespace XDrishti.Execution.Domain.Limits;

/// <summary>
/// One system-sent entry as the guard judges it: the intent and its plan ticket, the product and side, the quantity and the plan's allocation, the levels, the approval behind the ticket (<c>owner</c> or <c>auto</c>, null when unknown) and the
/// moment the trigger fired. <see cref="Notional"/> and <see cref="RiskAtStop"/> follow from them.
/// </summary>
public sealed record AutoEntry(
    Guid AccountId,
    Guid IntentPublicId,
    long PlanItemId,
    string Symbol,
    string StrategyHash,
    string Product,
    bool IsFutures,
    ExecSide Side,
    int Quantity,
    int? PlanAllocation,
    decimal LimitPrice,
    decimal Stop,
    decimal Target,
    string? ApprovalKind,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset TriggeredAt)
{
    public decimal Notional => decimal.Round(Quantity * LimitPrice, 2);

    public decimal RiskAtStop => decimal.Round(Quantity * Math.Max(0m, LimitPrice - Stop), 2);
}

/// <summary>The day so far in the ledger of an account: the entries reserved and their notional.</summary>
public sealed record LedgerDay(int Orders, decimal Notional);

/// <summary>
/// What the submit path has already read for an entry (spec P12-09 Non-functional: the guard is pure, its inputs prefetched): the plan's state, P12-03's verdict, readiness, reconciliation, halts, the day unlock, the account's exposure and the
/// day's loss figures. A null is a fact that could not be read: it never passes.
/// </summary>
public sealed record EntryFacts(
    bool? PlanProvisional,
    IReadOnlyList<string>? PlanUnavailableInputs,
    bool? PretradeAllPass,
    bool? Ready,
    bool? ReconBreakToday,
    bool? Halted,
    bool? DayUnlocked,
    bool? ExposureInSymbol,
    int? OpenPositions,
    decimal? RealisedToday,
    decimal? OpenRiskToStops);

/// <summary>Everything the guard reads for one <c>auto_live</c> entry: the sheet in force, the ledger day, the prefetched facts, whether the strategy version was used live by the account, the owner's newest session and the feed price time.</summary>
public sealed record AutoFacts(ExecutionLimits? Limits, LedgerDay? Day, EntryFacts Entry, bool? StrategyUsedLive, OwnerSeen? Owner, DateTimeOffset? FeedPriceAt);

/// <summary>
/// The guard's answer. <see cref="Silent"/> is the switch rule of criterion 12: the account is not an <c>auto_live</c> account with both switches ON, nothing is written and nothing is routed. <see cref="RouteToTicket"/> is true when the
/// only refusal is the strategy's first live use, which the always-ask list sends to a ticket as in <c>semi_auto</c>.
/// </summary>
public sealed record GuardVerdict(IReadOnlyList<string> Codes, bool Silent)
{
    public static readonly GuardVerdict NotAuto = new([AutoLiveCodes.NotAuto], true);

    public bool Passed => Codes.Count == 0;

    public bool RouteToTicket => Codes.Count == 1 && Codes[0] == AutoLiveCodes.StrategyFirstUse;
}

/// <summary>
/// The hard limits and the <c>auto_live</c> guard of spec P12-09 (criteria 1 to 7 and 12). <see cref="Caps"/> applies to every system-sent entry, in <c>semi_auto</c> as in <c>auto_live</c>; <see cref="Evaluate"/> runs only for an
/// <c>auto_live</c> account with both switches ON and adds the rules of an entry without a tap. Every refusal names its rule; every rule is listed, not only the first. The effective cap is always the stricter of the sheet and P12-01's global
/// value. The window is India time of the clock given (the database's), start included, end excluded. Pure: under 5 ms for any input.
/// </summary>
public static class AutoLiveGuard
{
    /// <summary>A feed price older than this, or none, is stale (criterion 6).</summary>
    public static readonly TimeSpan FeedMaxAge = TimeSpan.FromSeconds(15);

    /// <summary>The countable caps: the sheet complete, product, instrument, window, notional per order, orders a day and notional a day.</summary>
    public static IReadOnlyList<string> Caps(ExecutionLimits? limits, AutoLiveSettings settings, AutoEntry entry, LedgerDay? day, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(entry);
        if (limits is null || !limits.IsComplete || limits.Invalid() is not null)
        {
            return [AutoLiveCodes.LimitsIncomplete];
        }

        var codes = new List<string>();
        if (entry.IsFutures || entry.Side != ExecSide.Buy || entry.Product != LimitsCeilings.EquityIntraday || !limits.Products.Contains(entry.Product, StringComparer.Ordinal))
        {
            codes.Add(AutoLiveCodes.ProductNotAllowed);
        }

        if (!limits.Allows(entry.Symbol))
        {
            codes.Add(AutoLiveCodes.InstrumentNotAllowed);
        }

        if (!InWindow(limits, settings, now))
        {
            codes.Add(AutoLiveCodes.WindowClosed);
        }

        if (entry.Quantity <= 0 || entry.LimitPrice <= 0m || entry.Notional > Math.Min(limits.MaxNotionalPerOrder!.Value, settings.GlobalMaxOrderValue))
        {
            codes.Add(AutoLiveCodes.NotionalOrder);
        }

        if (day is null)
        {
            codes.Add(AutoLiveCodes.LedgerUnavailable);
            return codes;
        }

        if (day.Orders + 1 > Math.Min(limits.MaxOrdersPerDay!.Value, settings.GlobalMaxOrdersPerDay))
        {
            codes.Add(AutoLiveCodes.OrdersDay);
        }

        if (day.Notional + entry.Notional > limits.MaxNotionalPerDay!.Value)
        {
            codes.Add(AutoLiveCodes.NotionalDay);
        }

        return codes;
    }

    /// <summary>The whole guard for an entry without a tap. Not an <c>auto_live</c> account, or either switch OFF or unreadable: <see cref="GuardVerdict.NotAuto"/> after reading those alone.</summary>
    public static GuardVerdict Evaluate(ExecMode? mode, AutoLiveSettings? settings, AutoEntry entry, AutoFacts facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(facts);
        if (mode != ExecMode.AutoLive || settings is not { ExecutionEnabled: true, AutoLiveEnabled: true })
        {
            return GuardVerdict.NotAuto;
        }

        var f = facts.Entry;
        var codes = new List<string>(Caps(facts.Limits, settings, entry, facts.Day, now));
        if (facts.Limits is { IsComplete: true } limits && !codes.Contains(AutoLiveCodes.LimitsIncomplete))
        {
            if (f.OpenPositions is not { } open || open >= limits.MaxOpenPositions!.Value)
            {
                codes.Add(AutoLiveCodes.OpenPositions);
            }

            if (LossCap.Check(f.RealisedToday, f.OpenRiskToStops, entry.RiskAtStop, limits.MaxLossPerDay!.Value) is { } loss)
            {
                codes.Add(loss);
            }
        }

        Add(codes, !(entry.ApprovalKind == "owner" || (entry.ApprovalKind == "auto" && settings.AllowAutoApproved)), AutoLiveCodes.ApprovalNotOwner);
        Add(codes, f.PlanProvisional != false || f.PlanUnavailableInputs is not { Count: 0 }, AutoLiveCodes.PlanProvisional);
        Add(codes, entry.PlanAllocation is not { } allocation || entry.Quantity > allocation, AutoLiveCodes.QtyAbovePlan);
        Add(codes, f.ExposureInSymbol != false, AutoLiveCodes.ExposureDuplicate);
        Add(codes, facts.StrategyUsedLive != true, AutoLiveCodes.StrategyFirstUse);
        Add(codes, f.PretradeAllPass != true, AutoLiveCodes.PretradeFailed);
        Add(codes, settings.RequireDayUnlock && f.DayUnlocked != true, AutoLiveCodes.NotUnlocked);
        Add(codes, settings.RequireAttended && !OwnerPresence.IsAttended(facts.Owner, now, settings.AttendedMinutes), AutoLiveCodes.NotAttended);
        Add(codes, f.Ready != true, AutoLiveCodes.NotReady);
        Add(codes, !FeedFresh(facts.FeedPriceAt, now), AutoLiveCodes.FeedStale);
        Add(codes, f.ReconBreakToday != false, AutoLiveCodes.ReconBreak);
        Add(codes, f.Halted != false, AutoLiveCodes.Halted);
        return new GuardVerdict(codes, false);
    }

    /// <summary>Inside the stricter of the sheet's window and P12-01's global window, India time: start included, end excluded.</summary>
    public static bool InWindow(ExecutionLimits limits, AutoLiveSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(settings);
        if (limits.WindowStart is not { } sheetStart || limits.WindowEnd is not { } sheetEnd)
        {
            return false;
        }

        var start = sheetStart > settings.GlobalWindowStart ? sheetStart : settings.GlobalWindowStart;
        var end = sheetEnd < settings.GlobalWindowEnd ? sheetEnd : settings.GlobalWindowEnd;
        var time = TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime);
        return time >= start && time < end;
    }

    public static bool FeedFresh(DateTimeOffset? at, DateTimeOffset now) => at is { } t && t <= now + TimeSpan.FromSeconds(1) && now - t <= FeedMaxAge;

    private static void Add(List<string> codes, bool refused, string code)
    {
        if (refused)
        {
            codes.Add(code);
        }
    }
}
