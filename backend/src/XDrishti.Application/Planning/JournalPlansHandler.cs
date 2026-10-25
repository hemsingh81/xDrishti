using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Portfolio;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Planning;

/// <summary>
/// The auto-journal (spec P7-14, ADR 0093). For each past or current plan it reads the fills the existing portfolio sync stored, matches them to the plan's approved tickets with the pure <see cref="JournalMatcher"/>, stores the links
/// insert-only, then grades finished round trips with the pure <see cref="FillGrader"/> beside the plan grade. It reads fills and the plan record only: no ticket changes state, no portfolio row is written, no ticket is made from a fill,
/// and nothing here can place an order.
/// </summary>
internal sealed class JournalPlansHandler(
    IPlanStore plans,
    IJournalStore journal,
    IGradeStore grades,
    TimeProvider time,
    ILogger<JournalPlansHandler> logger) : ICommandHandler<JournalPlansCommand, JournalOutcome>
{
    private const int MaxPlans = 30;
    private const int MaxOpenEntries = 500;

    public async ValueTask<Result<JournalOutcome>> HandleAsync(JournalPlansCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (command.Date is { } d && d > today)
        {
            return Result.Failure<JournalOutcome>(PlanErrors.Invalid([new FieldError("date", "Fills cannot be journaled for a session that has not begun; the date must not be after today.")]));
        }

        // The newest version of each date, oldest session first, so that an exit fill is linked by the session of its entry before a later session could call it discretionary.
        var rows = (await plans.ListAsync(command.Date, null, MaxPlans, cancellationToken))
            .Where(r => r.Plan.ForDate <= today)
            .GroupBy(r => r.Plan.ForDate)
            .Select(g => g.OrderByDescending(r => r.Plan.Version).ThenByDescending(r => r.Id).First())
            .OrderBy(r => r.Plan.ForDate)
            .ToList();
        // One pass over every session: all entries are matched before any exit, so a fill that is a later session's entry is never an earlier session's exit.
        var tickets = new List<JournalTicket>();
        foreach (var plan in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tickets.AddRange(await TicketsAsync(plan, cancellationToken));
        }

        var dates = rows.Select(r => r.Plan.ForDate).ToList();
        var fills = dates.Count == 0 ? [] : await journal.FillsAsync(dates.Min(), Last(dates.Max(), today), cancellationToken);
        var result = JournalMatcher.Match(dates, tickets, fills);
        var byItem = tickets.GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => g.First());
        var entryKeys = result.Links.Where(l => l.Role == JournalRole.Entry).Select(l => (Item: l.ItemId!.Value, l.AccountId)).Distinct().ToList();
        var positions = await journal.PositionsBeforeAsync([.. entryKeys.Select(k => (k.AccountId, byItem[k.Item].Symbol, byItem[k.Item].ForDate))], cancellationToken);
        var uncertain = entryKeys.ToDictionary(k => k, k =>
            positions.GetValueOrDefault((k.AccountId, byItem[k.Item].Symbol, byItem[k.Item].ForDate)) is var held && held != 0
                ? "uncertain: pre-existing position (the account's earlier trades leave a net " + held.ToString(CultureInfo.InvariantCulture) + " shares of " + byItem[k.Item].Symbol + " open before this entry)"
                : null);
        var links = result.Links.Select(l => l.ItemId is { } id
            ? new NewJournalLink(byItem[id].PlanId, byItem[id].ForDate, l, l.Role == JournalRole.Entry ? uncertain[(id, l.AccountId)] : null)
            : new NewJournalLink(0, l.Fill.Date, l)).ToList();
        var saved = await journal.SaveAsync(links, now, cancellationToken);
        var unmatched = result.Unmatched.Select(u => new UnmatchedFill(u.Fill.Date, u.Fill.Symbol, u.Fill.Side == Side.Buy ? "buy" : "sell", u.Fill.Qty, u.Reason)).ToList();
        if (unmatched.Count > 0)
        {
            JournalLog.Unmatched(logger, rows.Count, unmatched.Count);
        }

        var entries = result.Links.Count(l => l.Role == JournalRole.Entry);
        var exits = result.Links.Count(l => l.Role == JournalRole.Exit);
        var discretionary = result.Links.Count(l => l.Role == JournalRole.Discretionary);
        var written = saved.Entries + saved.Fills;
        var ticketCount = tickets.Count;
        var (graded, pending, uncertainGrades, truncated) = await GradeAsync(now, cancellationToken);
        JournalLog.Done(logger, rows.Count, ticketCount, entries, exits, discretionary, unmatched.Count, written, graded);
        return Result.Success(new JournalOutcome(rows.Count, ticketCount, entries, exits, discretionary, written, graded, pending, unmatched, uncertainGrades, truncated));
    }

    /// <summary>One ticket per approved item and paper allocation. Reserve tickets count when the owner approved them; a rejected candidate has no decision.</summary>
    private async Task<List<JournalTicket>> TicketsAsync(PlanRow plan, CancellationToken cancellationToken)
    {
        var items = await plans.ItemsAsync(plan.Id, cancellationToken);
        var approvedAt = (await plans.HistoryAsync(plan.Id, cancellationToken))
            .Where(e => e.ItemId is not null && e.To == TicketState.Approved.Code())
            .GroupBy(e => e.ItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(e => e.At));
        var result = new List<JournalTicket>();
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected && TicketLifecycle.ApprovedFamily.Contains(i.State) && approvedAt.ContainsKey(i.Id)))
        {
            if (TicketLifecycle.ParseSide(item.Item.Side) is not { } side)
            {
                continue;
            }

            foreach (var allocation in item.Item.Allocations)
            {
                result.Add(new JournalTicket(item.Id, plan.Id, item.Item.Rank, allocation.AccountId, TradeRules.NormaliseSymbol(item.Item.Symbol), side, plan.Plan.ForDate, approvedAt[item.Id]));
            }
        }

        return result;
    }

    /// <summary>Fills are read from the first session to the earlier of today and the exit window after the last session.</summary>
    private static DateOnly Last(DateOnly lastSession, DateOnly today) => lastSession.AddDays(JournalMatcher.MaxExitDays) < today ? lastSession.AddDays(JournalMatcher.MaxExitDays) : today;

    private async Task<(int Graded, int Pending, int Uncertain, bool Truncated)> GradeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var fetched = await journal.OpenEntriesAsync(MaxOpenEntries + 1, cancellationToken);
        var truncated = fetched.Count > MaxOpenEntries;
        var open = truncated ? [.. fetched.TakeLast(MaxOpenEntries)] : fetched; // newest first was fetched; the oldest extra one is left for a later run
        if (open.Count == 0)
        {
            return (0, 0, 0, false);
        }

        var planGrades = await grades.ForPlansAsync([.. open.Select(e => e.PlanId).Distinct()], cancellationToken);
        var batch = new List<NewFillGrade>();
        var pending = 0;
        foreach (var entry in open)
        {
            var item = await plans.FindItemAsync(entry.ItemId, cancellationToken);
            if (item is null || TicketLifecycle.ParseSide(item.Item.Side) is not { } side)
            {
                pending++;
                continue;
            }

            if (!planGrades.TryGetValue(entry.ItemId, out var stored))
            {
                // The plan-price grade comes first, so that a fill grade is always stored beside the grade it is read against; this one is written on a later run.
                pending++;
                JournalLog.GradePending(logger, entry.Id, "the plan-price grade is not stored yet");
                continue;
            }

            var links = entry.Fills.Select(f => new JournalLink(Fill(f), JournalRoleText.TryParse(f.Role) ?? JournalRole.Discretionary, entry.ItemId, f.AccountId, f.Qty, f.Reason)).ToList();
            var grade = FillGrader.Grade(side, [.. links.Where(l => l.Role == JournalRole.Entry)], [.. links.Where(l => l.Role == JournalRole.Exit)], stored.Grade.Grade, entry.Uncertain);
            if (grade is null)
            {
                pending++;
                JournalLog.GradePending(logger, entry.Id, "the position is still open");
                continue;
            }

            batch.Add(new NewFillGrade(entry.Id, grade, now));
        }

        var written = batch.Count > 0 ? await journal.InsertGradesAsync(batch, cancellationToken) : 0;
        return (written, pending, batch.Count(g => g.Grade.Status == FillGrade.Uncertain), truncated);
    }

    internal static JournalFill Fill(StoredFill f) =>
        new(f.TransactionId, f.AccountId, f.Symbol, f.Side == "buy" ? Side.Buy : Side.Sell, f.Date, f.Time, f.TradeQty, f.Price, f.Charges);
}
