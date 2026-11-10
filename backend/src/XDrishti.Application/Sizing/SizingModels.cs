using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Sizing;

/// <summary>One change of a sizing setting (or a budget) in the history.</summary>
public sealed record SizingChangeView(long Id, string Key, string OldValue, string NewValue, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>
/// The sizing settings in force with their history (newest first, the latest 50). With no row everything is OFF. <see cref="LatestLoosening"/> is the newest change that can only raise a suggested quantity or remove a limit, or null when none was ever made.
/// </summary>
/// <param name="VolTargetEnabled"><c>sizing.vol_target.enabled</c>.</param>
/// <param name="Floor"><c>sizing.vol_target.floor</c>.</param>
/// <param name="CapacityEnabled"><c>sizing.capacity.enabled</c>.</param>
/// <param name="CapacityPct"><c>sizing.capacity.pct</c>.</param>
/// <param name="Version">The newest settings row id (0 when none).</param>
/// <param name="Notice">Down-only, advisory, no order.</param>
/// <param name="LatestLoosening">The newest change with direction loosen.</param>
/// <param name="History">The changes, newest first.</param>
public sealed record SizingSettingsView(bool VolTargetEnabled, double Floor, bool CapacityEnabled, double CapacityPct, long Version, string Notice, SizingChangeView? LatestLoosening, IReadOnlyList<SizingChangeView> History);

/// <summary>The budget of one basket: its share of the total open risk limit, in percent.</summary>
public sealed record BasketBudgetView(Guid BasketId, double BudgetPct);

/// <summary>One change of a basket budget; <paramref name="BudgetPct"/> null means the budget was removed.</summary>
public sealed record BudgetChangeView(long Id, Guid BasketId, double? BudgetPct, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>The basket budgets in force (a basket with none has no budget) and their history, newest first (the latest 50).</summary>
public sealed record BasketRiskBudgetsView(IReadOnlyList<BasketBudgetView> Budgets, string Notice, IReadOnlyList<BudgetChangeView> History);

/// <summary>Reads the sizing settings and their history.</summary>
public sealed record GetSizingSettingsQuery : IQuery<SizingSettingsView>;

/// <summary>Reads the basket budgets and their history.</summary>
public sealed record GetBasketRiskBudgetsQuery : IQuery<BasketRiskBudgetsView>;

/// <summary>
/// The owner's change of the sizing settings (BK-10). The endpoint checked the session and the CSRF token and built the actor from the session; the fresh password is checked here for EVERY change, without opening the elevation window. A value left null stays.
/// </summary>
public sealed record ChangeSizingSettingsCommand(string Actor, Guid SessionId, ClientInfo Client, bool? VolTargetEnabled, double? Floor, bool? CapacityEnabled, double? CapacityPct, string? Password) : ICommand;

/// <summary>The owner's change of one basket's budget (BK-10), 1 to 100 or null to remove it; the same checks as <see cref="ChangeSizingSettingsCommand"/>.</summary>
public sealed record ChangeBasketRiskBudgetCommand(string Actor, Guid SessionId, ClientInfo Client, Guid BasketId, double? BudgetPct, string? Password) : ICommand;

/// <summary>
/// What the stored trades would have done with the down scale (read only, descriptive, before costs, same trades).
/// </summary>
/// <param name="Run">The run key.</param>
/// <param name="Floor">The floor used.</param>
/// <param name="FloorSource"><c>setting</c> (the stored floor) or <c>sensitivity</c> (one of 0.25, 0.5, 0.75 asked for).</param>
/// <param name="WithoutScale">Trades with no scale (too little history): taken at scale 1.</param>
/// <param name="SkippedNoNetR">Closed trades with no net R (a gross run): left out.</param>
/// <param name="Result">The comparison.</param>
/// <param name="Caveat">Descriptive, before costs, same trades; nothing here is a recommendation.</param>
public sealed record SizingStudyView(string Run, double Floor, string FloorSource, int WithoutScale, int SkippedNoNetR, SizingStudyResult Result, string Caveat);

/// <summary>The sizing study of a stored run: <c>reports.invalid</c> for a bad run key, date range or floor.</summary>
/// <param name="Run">The run key (64 hex characters).</param>
/// <param name="From">First entry date, optional.</param>
/// <param name="To">Last entry date, optional.</param>
/// <param name="Floor">One of 0.25, 0.5, 0.75 as a labelled sensitivity; null uses the stored floor.</param>
public sealed record GetSizingStudyQuery(string? Run, DateOnly? From, DateOnly? To, double? Floor) : IQuery<Result<SizingStudyView>>;
