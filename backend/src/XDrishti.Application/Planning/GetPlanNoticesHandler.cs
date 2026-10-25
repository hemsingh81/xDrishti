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
internal sealed class GetPlanNoticesHandler(IPlanStore plans, IJournalStore journal, ITicketNoticeSource ticketNotices, TimeProvider clock) : IQueryHandler<GetPlanNoticesQuery, PlanNotices>
{
    /// <summary>The notices cover the last this many India days: today and the days before it, so three means today, yesterday and the day before. A plan or a failed run of an earlier session is not mentioned any more.</summary>
    public const int RecentDays = 3;

    private const int MaxSessions = 3;
    private const int ListLimit = 20;

    public async ValueTask<PlanNotices> HandleAsync(GetPlanNoticesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var from = today.AddDays(1 - RecentDays);

        var runRows = await plans.ListRunsAsync(null, 1, cancellationToken);
        var latestRun = runRows.Count > 0 && runRows[0].Run is { } run && run.ForDate >= from
            ? new PlanNoticeRun(run.ForDate, run.Outcome, run.FailedGate, run.Reason, run.FinishedAt, runRows[0].Attempt)
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
        var counts = (await ticketNotices.CountsAsync([.. withItems.Select(p => p.Row.Id)], now, cancellationToken)).ToDictionary(c => c.PlanId);
        var facts = withItems.Select(p => new PlanNoticePlan(
            p.Row.Id,
            p.Row.Plan.ForDate,
            p.Row.Plan.ReviewCutoff,
            p.Row.State,
            p.Items.Count(i => i.State is TicketState.Proposed or TicketState.Modified),
            p.Items.Count(i => i.State == TicketState.Lapsed),
            p.Items.Count(i => i.State == TicketState.Armed),
            p.Items.Count(i => i.State == TicketState.Invalidated),
            unmatched.GetValueOrDefault(p.Row.Id).Would,
            unmatched.GetValueOrDefault(p.Row.Id).Ambiguous,
            counts.TryGetValue(p.Row.Id, out var c) ? c.Crossings : 0,
            counts.TryGetValue(p.Row.Id, out c) ? c.Prompts : 0)).ToList();
        return new PlanNotices(PlanNoticeRules.Derive(now, facts, latestRun));
    }

    /// <summary>
    /// Per plan, what the journal would do with the stored fills of the session day, by the pure <see cref="JournalMatcher"/> over the tickets approved before the fill (the same rule the journal applies):
    /// the approved tickets that would get an entry and have none stored yet, and the fills that fit more than one ticket (never linked). A fill before the approval is discretionary and counts for neither.
    /// </summary>
    private async Task<Dictionary<long, (int Would, int Ambiguous)>> UnmatchedAsync(List<(PlanRow Row, IReadOnlyList<PlanItemRow> Items)> plansWithItems, DateOnly today, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, (int, int)>();
        var candidates = plansWithItems.Where(p => p.Row.Plan.ForDate <= today && p.Items.Any(i => TicketLifecycle.ApprovedFamily.Contains(i.State))).ToList();
        if (candidates.Count == 0)
        {
            return result;
        }

        var fills = await journal.FillsAsync(candidates.Min(p => p.Row.Plan.ForDate), candidates.Max(p => p.Row.Plan.ForDate), cancellationToken);
        foreach (var (row, items) in candidates)
        {
            var day = row.Plan.ForDate;
            var dayFills = fills.Where(f => f.Date == day).ToList();
            if (dayFills.Count == 0)
            {
                continue;
            }

            var approvedAt = (await plans.HistoryAsync(row.Id, cancellationToken))
                .Where(e => e.ItemId is not null && e.To == TicketState.Approved.Code())
                .GroupBy(e => e.ItemId!.Value)
                .ToDictionary(g => g.Key, g => g.Min(e => e.At));
            var tickets = items
                .Where(i => TicketLifecycle.ApprovedFamily.Contains(i.State) && approvedAt.ContainsKey(i.Id) && TicketLifecycle.ParseSide(i.Item.Side) is not null)
                .SelectMany(i => i.Item.Allocations.Select(a => new JournalTicket(i.Id, row.Id, i.Item.Rank, a.AccountId, TradeRules.NormaliseSymbol(i.Item.Symbol), TicketLifecycle.ParseSide(i.Item.Side)!.Value, day, approvedAt[i.Id])))
                .ToList();
            if (tickets.Count == 0)
            {
                continue;
            }

            var matched = JournalMatcher.Match(day, tickets, dayFills);
            var wanted = matched.Links.Where(l => l.Role == JournalRole.Entry && l.ItemId is not null).Select(l => (Item: l.ItemId!.Value, l.AccountId)).Distinct().ToList();
            var ambiguous = matched.Unmatched.Count;
            if (wanted.Count == 0 && ambiguous == 0)
            {
                continue;
            }

            var entered = wanted.Count == 0 ? [] : (await journal.EntriesAsync(row.Id, cancellationToken)).Select(e => (e.ItemId, e.AccountId)).ToHashSet();
            var would = wanted.Where(t => !entered.Contains(t)).Select(t => t.Item).Distinct().Count();
            if (would > 0 || ambiguous > 0)
            {
                result[row.Id] = (would, ambiguous);
            }
        }

        return result;
    }
}
