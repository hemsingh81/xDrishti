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

    private static readonly TicketState[] ApprovedFamily = [TicketState.Approved, TicketState.Armed, TicketState.Triggered, TicketState.Closed];

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
        int tickets = 0, entries = 0, exits = 0, discretionary = 0, written = 0;
        var unmatched = new List<UnmatchedFill>();
        foreach (var plan in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = plan.Plan.ForDate;
            var mine = await TicketsAsync(plan, cancellationToken);
            tickets += mine.Count;
            var last = date.AddDays(JournalMatcher.MaxExitDays) < today ? date.AddDays(JournalMatcher.MaxExitDays) : today;
            var fills = await journal.FillsAsync(date, last, cancellationToken);
            var result = JournalMatcher.Match(date, mine, fills);
            var planOf = mine.ToDictionary(t => t.ItemId, t => t.PlanId);
            var links = result.Links.Select(l => new NewJournalLink(l.ItemId is { } id && planOf.TryGetValue(id, out var p) ? p : 0, date, l)).ToList();
            var saved = await journal.SaveAsync(links, now, cancellationToken);
            written += saved.Entries + saved.Fills;
            entries += result.Links.Count(l => l.Role == JournalRole.Entry);
            exits += result.Links.Count(l => l.Role == JournalRole.Exit);
            discretionary += result.Links.Count(l => l.Role == JournalRole.Discretionary);
            unmatched.AddRange(result.Unmatched.Select(u => new UnmatchedFill(u.Fill.Date, u.Fill.Symbol, u.Fill.Side == Side.Buy ? "buy" : "sell", u.Fill.Qty, u.Reason)));
            if (result.Unmatched.Count > 0)
            {
                JournalLog.Unmatched(logger, plan.Id, result.Unmatched.Count);
            }
        }

        var (graded, pending) = await GradeAsync(now, cancellationToken);
        JournalLog.Done(logger, rows.Count, tickets, entries, exits, discretionary, unmatched.Count, written, graded);
        return Result.Success(new JournalOutcome(rows.Count, tickets, entries, exits, discretionary, written, graded, pending, unmatched));
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
        foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected && ApprovedFamily.Contains(i.State) && approvedAt.ContainsKey(i.Id)))
        {
            if (ParseSide(item.Item.Side) is not { } side)
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

    private async Task<(int Graded, int Pending)> GradeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await journal.OpenEntriesAsync(MaxOpenEntries, cancellationToken);
        if (open.Count == 0)
        {
            return (0, 0);
        }

        var planGrades = await grades.ForPlansAsync([.. open.Select(e => e.PlanId).Distinct()], cancellationToken);
        var batch = new List<NewFillGrade>();
        var pending = 0;
        foreach (var entry in open)
        {
            var item = await plans.FindItemAsync(entry.ItemId, cancellationToken);
            if (item is null || ParseSide(item.Item.Side) is not { } side)
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
            var grade = FillGrader.Grade(side, [.. links.Where(l => l.Role == JournalRole.Entry)], [.. links.Where(l => l.Role == JournalRole.Exit)], stored.Grade.Grade);
            if (grade is null)
            {
                pending++;
                JournalLog.GradePending(logger, entry.Id, "the position is still open");
                continue;
            }

            batch.Add(new NewFillGrade(entry.Id, grade, now));
        }

        var written = batch.Count > 0 ? await journal.InsertGradesAsync(batch, cancellationToken) : 0;
        return (written, pending);
    }

    internal static JournalFill Fill(StoredFill f) =>
        new(f.TransactionId, f.AccountId, f.Symbol, f.Side == "buy" ? Side.Buy : Side.Sell, f.Date, f.Time, f.TradeQty, f.Price, f.Charges);

    internal static Side? ParseSide(string side) => side switch
    {
        "BUY" => Side.Buy,
        "SELL" => Side.Sell,
        _ => null,
    };
}
