using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The alerts of one plan for the ticket cards (spec P7-20). Read only; empty while <c>Planning:TicketAlerts:Enabled</c> is false.</summary>
internal sealed class GetPlanAlertsHandler(IPlanStore plans, TicketAlertReader reader, ITicketAlertSettings options, TimeProvider clock) : IQueryHandler<GetPlanAlertsQuery, Result<PlanAlertsView>>
{
    public async ValueTask<Result<PlanAlertsView>> HandleAsync(GetPlanAlertsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = await plans.FindAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PlanAlertsView>(PlanErrors.NotFound(query.PlanId));
        }

        if (!options.Enabled)
        {
            return Result.Success(new PlanAlertsView(false, [], [], []));
        }

        var (crossings, prompts, muted) = await reader.ReadAsync(plan.Plan.ForDate, query.PlanId, options.PromptTradingDays, clock.GetUtcNow(), cancellationToken);
        return Result.Success(new PlanAlertsView(true, crossings, prompts, muted));
    }
}

/// <summary>Mutes the further crossings of a ticket for its session: one insert-only act of the owner. A ticket never changes.</summary>
internal sealed class MuteTicketAlertsHandler(ITicketAlertStore store, TimeProvider clock) : ICommandHandler<MuteTicketAlertsCommand>
{
    public async ValueTask<Result> HandleAsync(MuteTicketAlertsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var item = await store.FindItemAsync(command.ItemId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound(PlanErrors.NotFoundCode, $"No plan item {command.ItemId} is recorded."));
        }

        var now = clock.GetUtcNow();
        var ist = IndiaTime.ToIst(now);
        if (item.Session < IndiaTime.Today(now) || (item.Session == IndiaTime.Today(now) && ist.TimeOfDay >= TicketCrossing.SessionClose))
        {
            return Result.Failure(Error.Conflict(TicketAlertErrors.AlertClosedCode, "The session of this ticket is over; there is nothing left to mute."));
        }

        if ((await store.ListAcksAsync(cancellationToken)).Any(a => a is { NoticeKind: "ticket", Action: AlertAcks.Mute } && a.ItemId == command.ItemId))
        {
            return Result.Success();
        }

        await store.AddAckAsync(new NewAck("ticket", null, command.ItemId, AlertAcks.Mute, null, command.Actor), cancellationToken);
        return Result.Success();
    }
}

/// <summary>Hides a crossing or a prompt for 15, 30 or 60 minutes, at most three times. Insert-only.</summary>
internal sealed class SnoozeTicketNoticeHandler(ITicketAlertStore store, TimeProvider clock) : ICommandHandler<SnoozeTicketNoticeCommand>
{
    public async ValueTask<Result> HandleAsync(SnoozeTicketNoticeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!AlertAcks.SnoozeMinutes.Contains(command.Minutes))
        {
            return Result.Failure(Error.Validation(PlanErrors.InvalidCode, "Snooze for 15, 30 or 60 minutes."));
        }

        if (TicketAlertErrors.ParseNotice(command.NoticeId) is not var (kind, id))
        {
            return Result.Failure(Error.Validation(PlanErrors.InvalidCode, "The notice id must be crossing:{id} or prompt:{id}."));
        }

        if (!await store.NoticeExistsAsync(kind, id, cancellationToken))
        {
            return Result.Failure(Error.NotFound(PlanErrors.NotFoundCode, "No such notice."));
        }

        var now = clock.GetUtcNow();
        var own = (await store.ListAcksAsync(cancellationToken)).Where(a => a.NoticeKind == kind && a.NoticeId == id).Select(a => new AlertAck(a.Id, a.Action, a.Until)).ToList();
        if (AlertAcks.Visibility(own, now) == AlertVisibility.Dismissed)
        {
            return Result.Failure(Error.Conflict(TicketAlertErrors.AlertClosedCode, "This notice was dismissed."));
        }

        // shortcut: the snooze count is read, then the act is inserted, not one atomic statement; one owner clicking can exceed the cap of three by a double click at worst, which only hides a notice for another 15-60 minutes.
        if (!AlertAcks.CanSnooze(own, now))
        {
            return Result.Failure(Error.Conflict(TicketAlertErrors.SnoozeLimitCode, $"A notice can be snoozed {AlertAcks.MaxSnoozes} times; dismiss it instead."));
        }

        await store.AddAckAsync(new NewAck(kind, id, null, AlertAcks.Snooze, now.AddMinutes(command.Minutes), command.Actor), cancellationToken);
        return Result.Success();
    }
}

/// <summary>Hides a crossing or a prompt for good. Insert-only; a second dismiss adds nothing.</summary>
internal sealed class DismissTicketNoticeHandler(ITicketAlertStore store, TimeProvider clock) : ICommandHandler<DismissTicketNoticeCommand>
{
    public async ValueTask<Result> HandleAsync(DismissTicketNoticeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (TicketAlertErrors.ParseNotice(command.NoticeId) is not var (kind, id))
        {
            return Result.Failure(Error.Validation(PlanErrors.InvalidCode, "The notice id must be crossing:{id} or prompt:{id}."));
        }

        if (!await store.NoticeExistsAsync(kind, id, cancellationToken))
        {
            return Result.Failure(Error.NotFound(PlanErrors.NotFoundCode, "No such notice."));
        }

        var own = (await store.ListAcksAsync(cancellationToken)).Where(a => a.NoticeKind == kind && a.NoticeId == id).Select(a => new AlertAck(a.Id, a.Action, a.Until)).ToList();
        if (AlertAcks.Visibility(own, clock.GetUtcNow()) != AlertVisibility.Dismissed)
        {
            await store.AddAckAsync(new NewAck(kind, id, null, AlertAcks.Dismiss, null, command.Actor), cancellationToken);
        }

        return Result.Success();
    }
}
