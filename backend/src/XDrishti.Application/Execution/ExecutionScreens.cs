using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Execution;

/// <summary>
/// A figure of the execution screens (spec P12-10 Contract, P8-00 D3). <see cref="State"/> is <c>value</c>, <c>insufficient</c>, <c>unavailable</c>, <c>not_evaluable</c> or <c>empty</c>; a digit exists only for <c>value</c>. An input that cannot be read is
/// <c>unavailable</c> with its <see cref="Reason"/>, never zero. <see cref="Cap"/> is the limit in force (null when the sheet leaves it unset, with the reason <c>cap_unset</c>).
/// </summary>
public sealed record ExecMetric(string State, decimal? Value = null, decimal? Cap = null, string? Reason = null, int? N = null)
{
    public static ExecMetric Of(decimal value, decimal? cap = null, string? reason = null, int? n = null) => new("value", value, cap, reason, n);

    public static ExecMetric Unavailable(string reason) => new("unavailable", Reason: reason);

    public static ExecMetric Empty(string reason) => new("empty", Reason: reason);
}

public sealed record OverviewReadiness(string State, DateTimeOffset? CheckedAt, DateTimeOffset? CoveredUntil);

/// <summary>The four meters of an account against the sheet in force; <see cref="PendingLimits"/> is the raise that waits for its time, shown beside it.</summary>
public sealed record OverviewCaps(ExecMetric Orders, ExecMetric Notional, ExecMetric WorstCaseLoss, ExecMetric OpenPositions, long? LimitsId, LimitsRowView? PendingLimits);

/// <summary>One account on Today. <see cref="Mode"/> is the effective mode and <see cref="EffectiveFrom"/> the time its trail row took effect (null: never changed from the configured one).</summary>
public sealed record OverviewAccount(
    Guid AccountId, string Name, string Mode, DateTimeOffset? EffectiveFrom, string? PendingMode, DateTimeOffset? PendingFrom, ExecutionGateView Gate, OverviewReadiness Readiness, OverviewCaps Caps,
    int OpenTickets, IReadOnlyList<ExecutionHaltView> Halts, IReadOnlyList<ExecutionAlarmView> Alarms, int PositionsAtRisk);

/// <summary><see cref="CanaryPending"/> is <c>exec.canary_pending</c> (null when it could not be read). <see cref="Limits"/> is the evidence line: what the numbers are and are not.</summary>
public sealed record ExecutionOverview(DateOnly Date, bool? CanaryPending, bool? ExecutionEnabled, IReadOnlyList<OverviewAccount> Accounts, IReadOnlyList<string> Limits);

public sealed record BlotterFill(DateTimeOffset At, int Quantity, decimal Price, ExecMetric SlippageTicks, ExecMetric SlippagePercent);

/// <summary>
/// One order of the day. <see cref="State"/> is P12-01's word, passed through unchanged when it is not one of the fourteen (<see cref="StateKnown"/> false). <see cref="By"/> is <c>tap</c>, <c>auto</c>, <c>system</c> or <c>owner</c>. Slippage is the fill's
/// price against the ticket price, positive when worse for the owner. <see cref="AlgoId"/> is only what the broker returned.
/// </summary>
public sealed record BlotterRow(
    Guid IntentId, DateTimeOffset At, Guid AccountId, string Account, string Symbol, string Side, int Quantity, string OrderType, decimal Price, string State, string StateGroup, bool StateKnown, string By, bool Canary,
    string? ClientOrderId, string? BrokerOrderId, string? AlgoId, int FilledQuantity, decimal? FillPrice, ExecMetric SlippageTicks, ExecMetric SlippagePercent, IReadOnlyList<BlotterFill> Fills,
    IReadOnlyList<string> RejectionCodes, string? BrokerCode);

/// <summary>
/// One open position. <see cref="Protection"/> is <c>confirmed</c> only when the broker's order book, read within <see cref="ExecutionScreens.ProtectionReadLimit"/>, was compared with our stop and showed no difference; <c>missing</c> when the engine raised a
/// protection breach; else <c>unknown</c>. <see cref="Source"/> is <c>ours</c> or <c>foreign</c>; <see cref="Reconciliation"/> is <c>matched</c>, <c>break</c>, <c>not_run</c> or <c>unavailable</c>.
/// </summary>
public sealed record PositionRow(
    Guid AccountId, string Account, string Symbol, int NetQuantity, decimal AveragePrice, ExecMetric LastPrice, ExecMetric Unrealised, ExecMetric RiskToStop, decimal? StopPrice, string Protection, string Source, string Reconciliation);

/// <summary>One line of the day's activity, newest first. <see cref="Kind"/> is an action-log kind or <c>alarm</c>, <c>halt</c>, <c>drill</c>, <c>mode_change</c>, <c>settings_change</c>; <see cref="By"/> is <c>auto</c>, <c>tap</c>, <c>system</c> or <c>owner</c>.</summary>
public sealed record ActivityRow(string Id, DateTimeOffset At, string Kind, string By, Guid? AccountId, string Text, IReadOnlyList<string> Codes);

/// <summary>An item for the bell, in the shape of the portfolio notices. The id includes the cause, so a changed situation is a new item. A resolved item stays 24 hours and is not counted.</summary>
public sealed record ExecutionNotice(string Id, string Kind, string Severity, string Text, string Title, DateTimeOffset At, IReadOnlyList<AttentionAction> Actions, DateTimeOffset Since, bool Resolved, Guid? AccountId);

public sealed record ExecutionNotices(IReadOnlyList<ExecutionNotice> Items);

// ---- raw rows the reader returns; every rule is applied by the handlers ----

/// <summary>What the record says about one account on one day. <see cref="Realised"/> is the day mark (null when there is none).</summary>
public sealed record DayAccountRaw(Guid AccountId, int Orders, decimal Notional, int OpenPositions, int OpenTickets, int Intents, int FilledEntries, decimal OpenRiskToStops, bool FillsInconsistent, decimal? Realised);

public sealed record BlotterFillRaw(DateTimeOffset At, int Quantity, decimal Price);

public sealed record BlotterRaw(
    Guid IntentId, Guid AccountId, string Account, DateTimeOffset At, string Symbol, string Side, int Quantity, decimal EntryPrice, string State, int FilledQuantity, string? ClientOrderId, string? BrokerOrderId, string? AlgoId,
    string? ActionBy, bool Assisted, string? BrokerCode, IReadOnlyList<string> RejectionCodes, IReadOnlyList<BlotterFillRaw> Fills);

public sealed record PositionRaw(
    Guid AccountId, string Account, string Symbol, int NetQuantity, decimal AveragePrice, decimal? StopPrice, string? IntentState, bool BreachOpen, bool HasIntent);

public sealed record NoticeTicketRaw(Guid Id, Guid AccountId, string Text, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string? Outcome);

public sealed record NoticeActionRaw(long Id, Guid? AccountId, string Kind, string Text, DateTimeOffset At);

public sealed record NoticeRejectedRaw(Guid IntentId, Guid AccountId, string Symbol, DateTimeOffset At, DateOnly SessionDate);

public sealed record NoticeRaw(IReadOnlyList<NoticeTicketRaw> Tickets, IReadOnlyList<NoticeActionRaw> AutoActions, IReadOnlyList<NoticeRejectedRaw> Rejected);

/// <summary>
/// The screens' reads of the execution record as the runtime role (SELECT only; spec P12-10). Everything is a bounded projection over day-sized tables. A fact that does not exist is null or absent, never zero. Halts, alarms, reconciliation, modes, readiness and
/// limits are read through their own ports.
/// </summary>
public interface IExecutionScreenReader
{
    /// <summary>Per account with a record of the day: orders and notional from the ledger, open positions now, open tickets at <paramref name="now"/>, the entry fills and the open risk to the stops.</summary>
    Task<IReadOnlyList<DayAccountRaw>> DayAsync(DateOnly date, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The value of <c>exec.canary_pending</c> and of <c>execution_enabled</c> in the newest settings row; null when there is no row or the key is missing and cannot be defaulted.</summary>
    Task<(bool? CanaryPending, bool? ExecutionEnabled)> SwitchesAsync(CancellationToken cancellationToken);

    /// <summary>Orders of the day, newest first. <paramref name="states"/> null keeps every state.</summary>
    Task<IReadOnlyList<BlotterRaw>> BlotterAsync(DateOnly date, Guid? accountId, IReadOnlyList<string>? states, int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<PositionRaw>> PositionsAsync(Guid? accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActivityRow>> ActivityAsync(DateOnly? date, string? kind, string? by, int skip, int take, CancellationToken cancellationToken);

    Task<NoticeRaw> NoticesAsync(DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken);
}

public static class ExecutionScreenErrors
{
    public const string Invalid = "execution.invalid";
}

public static partial class ExecutionScreens
{
    public const int PageSize = 50;
    public const int MaxPage = 1000;

    /// <summary>A broker read older than this is not proof that the stop is there.</summary>
    public static readonly TimeSpan ProtectionReadLimit = TimeSpan.FromSeconds(120);

    public static readonly string[] StateWords = ["planned", "armed", "checking", "rejected", "submitting", "open", "filled", "protected", "managing", "closing", "closed", "reconciled", "cancelled", "failed"];

    public static readonly string[] StateGroups = ["pending", "working", "live", "done", "stopped"];

    public static readonly string[] Kinds = ["auto_entry", "tap_entry", "auto_refused", "tap_refused", "auto_stop_tighten", "auto_exit", "auto_cancel", "loss_cap_halt", "loss_cap_notice", "alarm", "halt", "drill", "mode_change", "settings_change"];

    public static readonly string[] Actors = ["auto", "tap", "system", "owner"];

    /// <summary>The group of a state word, or <c>other</c> for a word outside the vocabulary.</summary>
    public static string GroupOf(string state) => state switch
    {
        "planned" or "armed" or "checking" => "pending",
        "submitting" or "open" => "working",
        "filled" or "protected" or "managing" or "closing" => "live",
        "closed" or "reconciled" => "done",
        "rejected" or "cancelled" or "failed" => "stopped",
        _ => "other",
    };

    public static string[] StatesOf(string group) => [.. StateWords.Where(s => GroupOf(s) == group)];

    public static Error Invalid(string message) => Error.Validation(ExecutionScreenErrors.Invalid, message);

    [LoggerMessage(EventId = 6730, Level = LogLevel.Debug, Message = "execution.overview_read accounts={Accounts}")]
    public static partial void OverviewRead(ILogger logger, int accounts);

    [LoggerMessage(EventId = 6731, Level = LogLevel.Debug, Message = "execution.blotter_read rows={Rows} page={Page}")]
    public static partial void BlotterRead(ILogger logger, int rows, int page);

    [LoggerMessage(EventId = 6732, Level = LogLevel.Debug, Message = "execution.positions_read rows={Rows}")]
    public static partial void PositionsRead(ILogger logger, int rows);

    [LoggerMessage(EventId = 6733, Level = LogLevel.Debug, Message = "execution.activity_read rows={Rows} page={Page}")]
    public static partial void ActivityRead(ILogger logger, int rows, int page);

    [LoggerMessage(EventId = 6734, Level = LogLevel.Debug, Message = "execution.notices_read items={Items} open={Open}")]
    public static partial void NoticesRead(ILogger logger, int items, int open);

    [LoggerMessage(EventId = 6735, Level = LogLevel.Information, Message = "execution.screen_refused code={Code}")]
    public static partial void Refused(ILogger logger, string code);
}

public sealed record GetExecutionOverviewQuery(DateOnly? Date) : IQuery<Result<ExecutionOverview>>;

public sealed record GetExecutionBlotterQuery(DateOnly? Date, Guid? AccountId, string? StateGroup, int Page) : IQuery<Result<IReadOnlyList<BlotterRow>>>;

public sealed record GetExecutionPositionsQuery(Guid? AccountId) : IQuery<Result<IReadOnlyList<PositionRow>>>;

public sealed record GetExecutionActivityQuery(DateOnly? Date, string? Kind, string? By, int Page) : IQuery<Result<IReadOnlyList<ActivityRow>>>;

public sealed record GetExecutionNoticesQuery : IQuery<ExecutionNotices>;
