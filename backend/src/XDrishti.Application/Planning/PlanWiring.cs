using System.Globalization;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Allocation;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Planning;

/// <summary>What the wiring made of one primary ticket: the rows to store and the standings that have no column of their own.</summary>
/// <param name="Allocations">Paper rows with a quantity above 0.</param>
/// <param name="Standings">Codes for the evidence list: <c>allocation:{code}</c> for an account that got nothing, <c>checklist:{code}:{outcome}</c> for a check that is not a pass.</param>
internal sealed record WiredTicket(IReadOnlyList<NewAllocation> Allocations, IReadOnlyList<string> Standings);

/// <summary>
/// Per-account allocation (P7-07) and the pre-trade checklist (P7-05) for the primary tickets, in plan order (spec P7-09, ADR 0092). Pure: everything comes in as arguments. Each ticket is sized against what the earlier tickets of the same plan
/// already took from the account (risk and funds), so one account is not given more than its limits across the whole plan. An input with no data source is passed as null, which the allocator and the checklist read as unavailable, never as a pass.
/// The entry price is the close of the session (indicative): tomorrow's price is not known.
/// </summary>
internal static class PlanWiring
{
    /// <summary>Said on every primary ticket: the sizes rest on the close of the session, not on tomorrow's entry.</summary>
    public const string IndicativeEntry = "allocation_entry_is_indicative";

    /// <summary>Said when the plan has no Trading account to size for.</summary>
    public const string NoTradingAccount = "allocation:no_trading_account";

    private static readonly ChecklistLimits Limits = ChecklistLimits.Default;

    public static IReadOnlyList<WiredTicket> Wire(IReadOnlyList<(FillCandidate Candidate, TicketDraft Draft)> primaries, PlanContext context, double riskMultiplier)
    {
        ArgumentNullException.ThrowIfNull(primaries);
        ArgumentNullException.ThrowIfNull(context);
        var addedRisk = new Dictionary<Guid, double>();
        var addedCost = new Dictionary<Guid, double>();
        var wired = new List<WiredTicket>(primaries.Count);
        foreach (var (candidate, draft) in primaries)
        {
            wired.Add(WireOne(candidate, draft, context, riskMultiplier, addedRisk, addedCost));
        }

        return wired;
    }

    private static WiredTicket WireOne(FillCandidate candidate, TicketDraft draft, PlanContext context, double riskMultiplier, Dictionary<Guid, double> addedRisk, Dictionary<Guid, double> addedCost)
    {
        if (context.Accounts.Count == 0)
        {
            return new WiredTicket([], [NoTradingAccount, "checklist:no_trading_account"]);
        }

        var symbol = candidate.Symbol;
        var facts = context.Instruments.TryGetValue(symbol.ToUpperInvariant(), out var f) ? f : null;
        var accounts = context.Accounts.Select(a => ToAllocationAccount(a, symbol, riskMultiplier, addedRisk, addedCost)).ToList();
        var ticket = new AllocationTicket(symbol, candidate.Side, draft.IndicativeEntry, draft.StopLevel, Pool(draft), 1);
        var result = PerAccountAllocator.Allocate(ticket, accounts);
        var standings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in result.Decisions.Where(d => !d.IsAllocated))
        {
            standings.Add("allocation:" + d.Code);
        }

        foreach (var account in context.Accounts)
        {
            var decision = result.Decisions.FirstOrDefault(d => d.AccountId == account.AccountId);
            var allocated = decision is { IsAllocated: true };
            var checklistTicket = new ChecklistTicket(symbol, candidate.Side, draft.IndicativeEntry, allocated ? decision!.Quantity : null, allocated ? decision!.RiskPct : null, facts?.Sector);
            var review = PreTradeChecklist.Evaluate(checklistTicket, ToChecklistAccount(account, facts, context, addedRisk, addedCost), ToFacts(symbol, facts, context), Limits, ChecklistPolicy.Default);
            foreach (var check in review.Checks.Where(c => c.Outcome != FilterOutcome.Pass))
            {
                standings.Add("checklist:" + check.Code + (check.Outcome == FilterOutcome.Unavailable ? ":unavailable" : ":fail"));
            }
        }

        foreach (var d in result.Allocated)
        {
            addedRisk[d.AccountId] = addedRisk.GetValueOrDefault(d.AccountId) + d.RiskPct;
            addedCost[d.AccountId] = addedCost.GetValueOrDefault(d.AccountId) + d.Quantity * (draft.IndicativeEntry ?? 0);
        }

        return new WiredTicket(AllocationRows.ToNew(result), [.. standings]);
    }

    // The ticket's pool: the share of the average daily volume the checklist allows (turnover / close), so the split can never hand out more than the liquidity check would pass. Unknown turnover or entry: no pool, nothing allocated.
    private static int? Pool(TicketDraft draft)
    {
        if (draft.AverageDailyTurnover is not { } turnover || draft.IndicativeEntry is not { } entry || !double.IsFinite(turnover) || !double.IsFinite(entry) || turnover <= 0 || entry <= 0)
        {
            return null;
        }

        var shares = Math.Floor(Limits.MaxPctOfAverageDailyVolume / 100 * turnover / entry);
        return shares < 1 ? null : (int)Math.Min(shares, int.MaxValue / 2);
    }

    private static AllocationAccount ToAllocationAccount(ContextAccount a, string symbol, double riskMultiplier, Dictionary<Guid, double> addedRisk, Dictionary<Guid, double> addedCost) => new(
        a.AccountId,
        a.Mode,
        true,
        a.Equity,
        a.RiskPerTradePct * riskMultiplier,
        a.MaxCapitalPerTradePct,
        a.MaxTotalRiskPct,
        a.OpenRiskPct + addedRisk.GetValueOrDefault(a.AccountId),
        a.FundsAvailable is { } funds ? Math.Max(0, funds - addedCost.GetValueOrDefault(a.AccountId)) : null,
        a.HeldSymbols?.Contains(symbol.ToUpperInvariant()));

    private static ChecklistAccount ToChecklistAccount(ContextAccount a, InstrumentFacts? facts, PlanContext context, Dictionary<Guid, double> addedRisk, Dictionary<Guid, double> addedCost)
    {
        int? sectorPositions = null;
        if (facts?.Sector is { } sector && context.Book is { } book && book.All(b => !string.IsNullOrWhiteSpace(b.Sector)))
        {
            sectorPositions = book.Count(b => string.Equals(b.Sector!.Trim(), sector.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return new ChecklistAccount(
            a.AccountId.ToString("D", CultureInfo.InvariantCulture),
            a.Mode,
            true,
            a.RiskPerTradePct,
            a.MaxTotalRiskPct,
            a.MaxPerSector,
            a.DailyLossLimitPct,
            a.FundsAvailable is { } funds ? Math.Max(0, funds - addedCost.GetValueOrDefault(a.AccountId)) : null,
            a.OpenRiskPct + addedRisk.GetValueOrDefault(a.AccountId),
            sectorPositions,
            a.DailyLossPct);
    }

    // Everything without a data source is null (unavailable): validation status, results dates, ban list, surveillance, price band, average volume, correlation with the book. The two that come from stored records are the holdings and the open book.
    private static ChecklistFacts ToFacts(string symbol, InstrumentFacts? facts, PlanContext context) => new(
        DataQualityOk: null,
        HeldLongAnyAccount: facts?.HeldLong,
        OpenTradeExists: context.Book?.Any(b => string.Equals(b.Symbol.Trim(), symbol.Trim(), StringComparison.OrdinalIgnoreCase)));
}
