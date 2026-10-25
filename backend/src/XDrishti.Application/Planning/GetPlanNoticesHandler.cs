using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Planning;

/// <summary>
/// The plan notices of the bell (P8-15, ADR 0101), derived on read like the data and service notices (ADR 0018 §7, ADR 0030): the newest version of each of the last three sessions' plans with the state of their tickets, the newest recorded attempt of the nightly pipeline
/// and the stored fills of approved tickets, handed to the pure <see cref="PlanNoticeRules"/>. It calls read members of the plan and journal ports only; nothing is stored, no ticket moves and no order exists anywhere.
/// </summary>
internal sealed class GetPlanNoticesHandler(IPlanStore plans, IJournalStore journal, TimeProvider clock) : IQueryHandler<GetPlanNoticesQuery, PlanNotices>
{
    /// <summary>A plan or a failed run older than this many India days is not mentioned any more.</summary>
    public const int RecentDays = 3;

    private const int MaxSessions = 3;
    private const int ListLimit = 20;

    private static readonly TicketState[] ApprovedFamily = [TicketState.Approved, TicketState.Armed, TicketState.Triggered, TicketState.Closed];

    public async ValueTask<PlanNotices> HandleAsync(GetPlanNoticesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var from = today.AddDays(-RecentDays);

        var runRows = await plans.ListRunsAsync(null, 1, cancellationToken);
        var latestRun = runRows.Count > 0 && runRows[0].Run is { } run && run.ForDate >= from
            ? new PlanNoticeRun(run.ForDate, run.Outcome, run.FailedGate, run.Reason, run.FinishedAt)
            : null;

        var rows = (await plans.ListAsync(null, null, ListLimit, cancellationToken))
            .Where(r => r.Plan.ForDate >= from)
            .GroupBy(r => r.Plan.ForDate)
            .Select(g => g.OrderByDescending(r => r.Plan.Version).ThenByDescending(r => r.Id).First())
            .OrderByDescending(r => r.Plan.ForDate)
            .Take(MaxSessions)
            .ToList();

        var withItems = new List<(PlanRow Row, IReadOnlyList<PlanItemRow> Items)>();
        foreach (var row in rows)
        {
            withItems.Add((row, [.. (await plans.ItemsAsync(row.Id, cancellationToken)).Where(i => i.Item.Group != TicketGroup.Rejected)]));
        }

        var unmatched = await UnmatchedAsync(withItems, today, cancellationToken);
        var facts = withItems.Select(p => new PlanNoticePlan(
            p.Row.Id,
            p.Row.Plan.ForDate,
            p.Row.Plan.ReviewCutoff,
            p.Row.State,
            p.Items.Count(i => i.State is TicketState.Proposed or TicketState.Modified),
            p.Items.Count(i => i.State == TicketState.Lapsed),
            p.Items.Count(i => i.State == TicketState.Armed),
            p.Items.Count(i => i.State == TicketState.Invalidated),
            unmatched.GetValueOrDefault(p.Row.Id))).ToList();
        return new PlanNotices(PlanNoticeRules.Derive(now, facts, latestRun));
    }

    /// <summary>Per plan, how many approved tickets have a stored fill on the session day (same account, normalised symbol and side) and no journal entry for that account yet.</summary>
    private async Task<Dictionary<long, int>> UnmatchedAsync(List<(PlanRow Row, IReadOnlyList<PlanItemRow> Items)> plansWithItems, DateOnly today, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, int>();
        var candidates = plansWithItems.Where(p => p.Row.Plan.ForDate <= today && p.Items.Any(i => ApprovedFamily.Contains(i.State))).ToList();
        if (candidates.Count == 0)
        {
            return result;
        }

        var fills = await journal.FillsAsync(candidates.Min(p => p.Row.Plan.ForDate), candidates.Max(p => p.Row.Plan.ForDate), cancellationToken);
        foreach (var (row, items) in candidates)
        {
            var day = row.Plan.ForDate;
            var wanted = items
                .Where(i => ApprovedFamily.Contains(i.State))
                .SelectMany(i => i.Item.Allocations.Select(a => (Item: i.Id, a.AccountId, Symbol: TradeRules.NormaliseSymbol(i.Item.Symbol), Side: JournalPlansHandler.ParseSide(i.Item.Side))))
                .Where(t => t.Side is not null && fills.Any(f => f.Date == day && f.AccountId == t.AccountId && f.Symbol == t.Symbol && f.Side == t.Side))
                .ToList();
            if (wanted.Count == 0)
            {
                continue;
            }

            var entered = (await journal.EntriesAsync(row.Id, cancellationToken)).Select(e => (e.ItemId, e.AccountId)).ToHashSet();
            var count = wanted.Where(t => !entered.Contains((t.Item, t.AccountId))).Select(t => t.Item).Distinct().Count();
            if (count > 0)
            {
                result[row.Id] = count;
            }
        }

        return result;
    }
}
