using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The end of the validity window (spec P7-11, doc 04 section 4.2): after the close of the plan's session (15:30 India time), every ticket still Armed, and every Approved one that was never armed, moves to Expired as the service actor, and
/// the plan with them. That removes the instruments from the live feed's set (the provider reads the Armed state). Plans of earlier sessions that are still open are closed the same way (the job was paused or the machine slept). Triggered
/// tickets are left alone. Idempotent: a closed plan is not touched. Approves nothing and places no order.
/// </summary>
internal sealed class ExpireTicketsHandler(
    IPlanStore store,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> tickets,
    ICommandHandler<MovePlanCommand, PlanMoveOutcome> plansMover,
    ILiveNotifier notifier,
    TimeProvider time,
    ILogger<ExpireTicketsHandler> logger) : ICommandHandler<ExpireTicketsCommand, ExpiryOutcome>
{
    /// <summary>The actor of the expiry: a service.</summary>
    public const string Actor = "system:expiry";

    private static readonly TimeSpan SessionClose = new(15, 30, 0);

    public async ValueTask<Result<ExpiryOutcome>> HandleAsync(ExpireTicketsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        var closed = IndiaTime.ToIst(now).TimeOfDay >= SessionClose;
        var open = (await store.ListAsync(null, null, 50, cancellationToken))
            .Where(p => p.State is PlanState.Approved or PlanState.Armed && (p.Plan.ForDate < today || (p.Plan.ForDate == today && closed)))
            .ToList();
        if (open.Count == 0)
        {
            return Result.Success(new ExpiryOutcome(0, 0, "No plan to close."));
        }

        var expired = 0;
        var plansClosed = 0;
        var failures = new List<string>();
        foreach (var plan in open)
        {
            var items = await store.ItemsAsync(plan.Id, cancellationToken);
            var here = 0;
            foreach (var item in items.Where(i => i.Item.Group != TicketGroup.Rejected && i.State is TicketState.Armed or TicketState.Approved))
            {
                var moved = await tickets.HandleAsync(new MoveTicketCommand(item.Id, TicketState.Expired, Actor, "The validity window ended with the session."), cancellationToken);
                if (moved.IsFailure)
                {
                    PreOpenLog.MoveFailed(logger, item.Id, moved.Error.Code);
                    failures.Add($"item {item.Id}: {moved.Error.Code}");
                    continue;
                }

                here++;
            }

            expired += here;
            var move = await plansMover.HandleAsync(new MovePlanCommand(plan.Id, PlanState.Expired, Actor, "The session ended."), cancellationToken);
            if (move.IsFailure)
            {
                PreOpenLog.MoveFailed(logger, plan.Id, move.Error.Code);
                failures.Add($"plan {plan.Id}: {move.Error.Code}");
                continue;
            }

            plansClosed++;
            var planCode = PlanState.Expired.Code();
            PreOpenLog.Expired(logger, plan.Id, here, planCode);
        }

        if (expired > 0)
        {
            await notifier.ActiveSetChangedAsync(cancellationToken);
        }

        var summary = $"{plansClosed} plan(s) closed, {expired} ticket(s) expired.";
        return failures.Count > 0
            ? Result.Failure<ExpiryOutcome>(Error.Conflict("plans.expiry_incomplete", $"{summary} {failures.Count} move(s) failed ({string.Join("; ", failures)})."))
            : Result.Success(new ExpiryOutcome(plansClosed, expired, summary));
    }
}
