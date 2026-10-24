using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The pre-open check (spec P7-11, ADR 0096, doc 10 section 7): for each ticket of today's plan that is Approved, read the pre-open price, the previous close and the average true range, decide with <see cref="PreOpenRules"/> and move the
/// ticket to Armed or Invalidated as the service actor (<see cref="Actor"/>, never the owner). Anything missing invalidates; nothing is armed on a missing input. More than <see cref="PreOpenRules.MaxArmedTickets"/> are refused with a reason, lowest rank
/// first, and the owner gets an inbox row for every invalidated ticket (insert-only). Only tickets that are already Approved are looked at: this handler approves nothing, never reads the reserve as a source of tickets and has no promotion path.
/// It runs only before the market opens (09:15 India time); afterwards it changes nothing. The plan takes the state its tickets support (Armed if one is armed, Expired if none is left). Idempotent: a ticket that is no longer Approved is not touched.
/// It places, modifies or cancels no order and talks to no broker write endpoint.
/// </summary>
internal sealed class RunPreOpenCheckHandler(
    IPlanStore store,
    IPreOpenSource source,
    ICommandHandler<MoveTicketCommand, TicketMoveOutcome> tickets,
    ICommandHandler<MovePlanCommand, PlanMoveOutcome> plansMover,
    ISuggestionStore inbox,
    ILiveNotifier notifier,
    TimeProvider time,
    ILogger<RunPreOpenCheckHandler> logger) : ICommandHandler<RunPreOpenCheckCommand, PreOpenOutcome>
{
    /// <summary>The actor of the arming and invalidation: a service.</summary>
    public const string Actor = "system:preopen";

    /// <summary>The kind of the inbox row for an invalidated ticket.</summary>
    public const string InvalidatedKind = "ticket_invalidated_preopen";

    private static readonly TimeSpan MarketOpen = new(9, 15, 0);

    public async ValueTask<Result<PreOpenOutcome>> HandleAsync(RunPreOpenCheckCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (IndiaTime.ToIst(now).TimeOfDay >= MarketOpen)
        {
            PreOpenLog.Refused(logger, "plans.preopen_late");
            return Result.Failure<PreOpenOutcome>(Error.Conflict("plans.preopen_late", "The market has opened (09:15); nothing is armed after that. Approved tickets expire at the close."));
        }

        var todays = (await store.ListAsync(today, null, 20, cancellationToken)).Where(p => p.Plan.ForDate == today && p.State is PlanState.Approved or PlanState.Armed).ToList();
        var work = new List<(PlanRow Plan, List<PlanItemRow> Approved)>();
        foreach (var plan in todays)
        {
            var approved = (await store.ItemsAsync(plan.Id, cancellationToken))
                .Where(i => i.Item.Group != TicketGroup.Rejected && i.State == TicketState.Approved)
                .OrderBy(i => i.Item.Rank)
                .ToList();
            if (approved.Count > 0 || plan.State == PlanState.Approved)
            {
                work.Add((plan, approved));
            }
        }

        if (work.Count == 0)
        {
            return Result.Success(new PreOpenOutcome(0, 0, 0, "No approved ticket to check today."));
        }

        var symbols = work.SelectMany(w => w.Approved).Select(i => i.Item.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var snapshot = symbols.Count == 0 ? new PreOpenSnapshot(new Dictionary<string, PreOpenReading>(), null) : await source.ReadAsync(symbols, today, cancellationToken);
        if (snapshot.Problem is not null)
        {
            PreOpenLog.SourceProblem(logger);
        }

        var armedTotal = 0;
        var invalidatedTotal = 0;
        var failures = new List<string>();
        foreach (var (plan, approved) in work)
        {
            var armedHere = (await store.ItemsAsync(plan.Id, cancellationToken)).Count(i => i.State == TicketState.Armed);
            var invalidatedHere = 0;
            foreach (var item in approved)
            {
                var decision = armedHere >= PreOpenRules.MaxArmedTickets
                    ? new PreOpenDecision(false, PreOpenCodes.CapExceeded, $"At most {PreOpenRules.MaxArmedTickets} tickets are armed in a session; this one (rank {item.Item.Rank}) was refused, the others are untouched.")
                    : PreOpenRules.Decide(snapshot.Readings.GetValueOrDefault(item.Item.Symbol) ?? new PreOpenReading(null, null, null));
                var to = decision.Arm ? TicketState.Armed : TicketState.Invalidated;
                var reason = decision.Arm ? $"{PlanWording.Armed} Pre-open: {decision.Detail}." : $"Invalidated at the pre-open check ({decision.Code}): {decision.Detail}";
                var moved = await tickets.HandleAsync(new MoveTicketCommand(item.Id, to, Actor, Truncate(reason, 500)), cancellationToken);
                if (moved.IsFailure)
                {
                    // Another move won (stale) or the table refused: report it and go on with the others.
                    PreOpenLog.MoveFailed(logger, item.Id, moved.Error.Code);
                    failures.Add($"item {item.Id}: {moved.Error.Code}");
                    continue;
                }

                if (decision.Arm)
                {
                    armedHere++;
                    armedTotal++;
                    continue;
                }

                invalidatedHere++;
                invalidatedTotal++;
                PreOpenLog.Invalidated(logger, item.Id, decision.Code);
                var text = Truncate(string.Create(CultureInfo.InvariantCulture, $"Ticket {item.Item.Rank} ({item.Item.Symbol}) was dropped at the pre-open check: {decision.Code}, {decision.Detail} It was not armed. No order was placed and none exists."), NewSuggestion.MaxTextLength);
                await inbox.InsertAsync(plan.Id, [new NewSuggestion(InvalidatedKind, "item-" + item.Id.ToString(CultureInfo.InvariantCulture), text)], now, cancellationToken);
            }

            await MovePlanAsync(plan, armedHere, failures, cancellationToken);
            PreOpenLog.Checked(logger, plan.Id, armedHere, invalidatedHere);
        }

        if (armedTotal > 0)
        {
            // Tells the feed at once to read the armed tickets (its one-minute recompute stays the safety net; with no listener this is harmless).
            await notifier.ActiveSetChangedAsync(cancellationToken);
        }

        var summary = $"{work.Count} plan(s): {armedTotal} armed, {invalidatedTotal} invalidated. Ban list, surveillance and price band were not checked (no data source).";
        if (snapshot.Problem is not null)
        {
            summary += " The price read failed: " + snapshot.Problem;
        }

        return failures.Count > 0
            ? Result.Failure<PreOpenOutcome>(Error.Conflict("plans.preopen_incomplete", $"{summary} {failures.Count} move(s) failed ({string.Join("; ", failures)})."))
            : Result.Success(new PreOpenOutcome(work.Count, armedTotal, invalidatedTotal, summary));
    }

    /// <summary>The plan takes the state its tickets support: Armed when one is armed, Expired when none is left alive.</summary>
    private async Task MovePlanAsync(PlanRow plan, int armedNow, List<string> failures, CancellationToken cancellationToken)
    {
        if (plan.State != PlanState.Approved)
        {
            return;
        }

        var items = await store.ItemsAsync(plan.Id, cancellationToken);
        var facts = PlanFacts.Of(items.Where(i => i.Item.Group != TicketGroup.Rejected).Select(i => i.State));
        PlanState? to = facts.Armed > 0 ? PlanState.Armed : facts.Approved == 0 ? PlanState.Expired : null;
        if (to is null)
        {
            return;
        }

        var reason = to == PlanState.Armed ? $"{armedNow} ticket(s) armed. {PlanWording.Armed}" : "No ticket passed the pre-open check; nothing is armed.";
        var moved = await plansMover.HandleAsync(new MovePlanCommand(plan.Id, to.Value, Actor, reason), cancellationToken);
        if (moved.IsFailure)
        {
            PreOpenLog.MoveFailed(logger, plan.Id, moved.Error.Code);
            failures.Add($"plan {plan.Id}: {moved.Error.Code}");
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "~";
}
