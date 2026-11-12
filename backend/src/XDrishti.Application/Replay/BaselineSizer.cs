using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Allocation;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>What the baseline took of one session and what it did not.</summary>
/// <param name="Taken">The items taken, each with its quantity, in the order of the candidates.</param>
/// <param name="Blocked">The candidates not taken, one reason each (never a selection reason).</param>
/// <param name="RiskPct">The risk of each trade in percent of equity, r = min(risk per trade, headroom / n); 0 when nothing was sized.</param>
internal sealed record BaselineOutcome(IReadOnlyList<(NewPlanItem Item, TicketCandidate Candidate)> Taken, IReadOnlyList<ReplayBlocked> Blocked, double RiskPct);

/// <summary>
/// The no-selection baseline of SR-02 (D1): every candidate the session found, with no filter, ranking, smart rule, cluster or position cap. Each of the n candidates risks r = min(risk per trade, headroom / n) of equity, where the headroom is the total-risk limit less the open risk;
/// the quantity is the live allocator's (risk, capital per trade, free cash), so the limits are the same as the selection arm's. A symbol already open, or a second cell of a symbol of this session, is not doubled (blocked as <c>capital</c>). Pure and deterministic: candidates are taken in symbol and cell order.
/// </summary>
internal static class BaselineSizer
{
    private const int Pool = 1_000_000_000;

    public static BaselineOutcome Size(IReadOnlyList<TicketCandidate> found, LedgerFigures figures, ReplayRiskProfile profile, ISet<string> heldSymbols)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(heldSymbols);
        var blocked = new List<ReplayBlocked>();
        var ordered = found.OrderBy(c => c.Draft.Symbol, StringComparer.Ordinal).ThenBy(c => c.Cell.Key, StringComparer.Ordinal).ToList();
        var usable = new List<TicketCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in ordered)
        {
            if (heldSymbols.Contains(c.Draft.Symbol) || !seen.Add(c.Draft.Symbol))
            {
                blocked.Add(new ReplayBlocked(c.Draft.Symbol, c.Cell.Key, BlockReason.Capital, "already_held"));
            }
            else
            {
                usable.Add(c);
            }
        }

        if (usable.Count == 0)
        {
            return new BaselineOutcome([], blocked, 0);
        }

        if (figures.Equity is not > 0)
        {
            blocked.AddRange(usable.Select(c => new ReplayBlocked(c.Draft.Symbol, c.Cell.Key, BlockReason.Capital, "no_equity")));
            return new BaselineOutcome([], blocked, 0);
        }

        // the live daily-loss limit (the pre-trade checklist's rule: reaching it stops new tickets)
        if (figures.DailyLossPct is { } loss && (double)loss >= profile.DailyLossPct)
        {
            blocked.AddRange(usable.Select(c => new ReplayBlocked(c.Draft.Symbol, c.Cell.Key, BlockReason.TotalRisk, "daily_loss")));
            return new BaselineOutcome([], blocked, 0);
        }

        var equity = (double)figures.Equity.Value;
        var open = (double)(figures.OpenRiskPct ?? 0m);
        var headroom = profile.MaxTotalRiskPct - open;
        if (headroom <= 0)
        {
            blocked.AddRange(usable.Select(c => new ReplayBlocked(c.Draft.Symbol, c.Cell.Key, BlockReason.TotalRisk, "no_risk_headroom")));
            return new BaselineOutcome([], blocked, 0);
        }

        var r = Math.Min(profile.RiskPerTradePct, headroom / usable.Count);
        var addedRisk = 0.0;
        var addedCost = 0.0;
        var funds = (double)(figures.FundsAvailable ?? 0m);
        var taken = new List<(NewPlanItem, TicketCandidate)>();
        foreach (var c in usable)
        {
            var d = c.Draft;
            var account = new AllocationAccount(ReplayBook.AccountId, TradingMode.Paper, true, equity, r, profile.MaxCapitalPerTradePct, profile.MaxTotalRiskPct, open + addedRisk, Math.Max(0, funds - addedCost), false); // held symbols are filtered above
            var result = PerAccountAllocator.Allocate(new AllocationTicket(d.Symbol, d.Side, d.IndicativeEntry, d.StopLevel, Pool, 1), [account]);
            var allocated = result.Allocated;
            var decision = allocated.Count == 0 ? null : allocated[0];
            if (decision is null || decision.Quantity < 1)
            {
                blocked.Add(new ReplayBlocked(d.Symbol, c.Cell.Key, BlockReason.Capital, "no_quantity"));
                continue;
            }

            addedRisk += decision.RiskPct;
            addedCost += decision.Quantity * (d.IndicativeEntry ?? 0);
            taken.Add((ItemOf(taken.Count + 1, c, decision.Quantity), c));
        }

        return new BaselineOutcome(taken, blocked, r);
    }

    private static NewPlanItem ItemOf(int rank, TicketCandidate c, int quantity)
    {
        var d = c.Draft;
        var stats = c.Stats;
        var evidence = new PlanEvidence(
            c.Cell.Key,
            stats?.N ?? 0,
            "baseline: no selection",
            "none",
            null,
            null,
            [],
            null,
            null,
            null,
            stats?.AsOf);
        return new NewPlanItem(
            rank, TicketGroup.Primary, d.Symbol, d.Side, d.StrategyId, d.StrategyVersionHash, d.TriggerText, d.StopText, d.TargetText, d.ValidityText, ["baseline_equal_risk"], evidence,
            [new NewAllocation(ReplayBook.AccountId, "paper", quantity)], Planning.SelectionPlanBuilder.LevelsOf(d).Levels);
    }
}
