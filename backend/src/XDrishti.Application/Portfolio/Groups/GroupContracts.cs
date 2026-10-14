using XDrishti.Application.Portfolio.Income;

namespace XDrishti.Application.Portfolio.Groups;

// Response shapes of the holding groups (ADR 0021). Percentages are percent numbers (16.1 = 16.1 %), money is in rupees. Value, Pnl and PnlPct are null for a group whose holdings
// all lack a usable price (XirrState "no_price"; they are left out of the figures exactly as in the summary and counted in Unpriced). WeightPct is the group's value over the scope's total
// value of priced holdings; null when either is unknown.

/// <summary>
/// One group. Key is the account id, bucket id, sector name or basket id, or <c>~none</c> for the sector "Unclassified" and the basket "No basket". Holdings counts every holding of the group in the scope;
/// Unpriced the open ones without a usable price. DayChangeExcluded counts the open holdings left out of the day change. Parts is set only when the request had a <c>stackBy</c>. Income is the group's dividends and trade
/// charges and the net returns (ADR 0026), null when none is recorded; a group by account also holds the account's own dividends and costs (those of no holding). The group with the key <c>~unallocated</c> (name "Unallocated", no holdings,
/// no value) is added last when dividends and costs belong to no group, so that the columns add up to the total.
/// </summary>
public sealed record HoldingGroup(
    string Key,
    string Name,
    int Holdings,
    int Unpriced,
    decimal Invested,
    decimal? Value,
    decimal? Pnl,
    decimal? PnlPct,
    decimal Realised,
    decimal? Xirr,
    string XirrState,
    decimal? DayChange,
    decimal? DayChangePct,
    int DayChangeExcluded,
    decimal? WeightPct,
    IReadOnlyList<GroupPart>? Parts,
    IncomeView? Income = null);

/// <summary>The scope's totals, computed once over every holding of the scope (for basket groupings this is not the sum of the groups). WeightPct is 100 when the scope has a value. Income includes the dividends and costs of no holding.</summary>
public sealed record HoldingGroupTotal(
    int Holdings,
    int Unpriced,
    decimal Invested,
    decimal? Value,
    decimal? Pnl,
    decimal? PnlPct,
    decimal Realised,
    decimal? Xirr,
    string XirrState,
    decimal? DayChange,
    decimal? DayChangePct,
    int DayChangeExcluded,
    decimal? WeightPct,
    IncomeView? Income = null);

/// <summary>The value of the holdings of a group that fall in one part of the second dimension (an account for sectors, a sector for accounts); the parts sum to the group's value.</summary>
public sealed record GroupPart(string Key, string Name, decimal Value);

/// <summary>
/// The groups of the scope. By is <c>account</c>, <c>bucket</c>, <c>sector</c> or <c>basket</c>; Additive is false for baskets (a holding is in every basket that contains it, so the groups overlap). StackBy is the second dimension of the
/// parts, when asked for. At most 200 groups are returned.
/// </summary>
public sealed record HoldingGroups(string By, bool Additive, string? StackBy, IReadOnlyList<HoldingGroup> Groups, HoldingGroupTotal Total);
