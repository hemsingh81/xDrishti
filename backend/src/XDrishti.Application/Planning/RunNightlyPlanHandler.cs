using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The fail-closed nightly pipeline (spec P7-08, ADR 0086). Order: the session and the planned session from the calendar; a published plan for it already means nothing is done; the gates in order (a real run stops at the first
/// that is not a pass, a dry run reads them all); the plan builder; the store. Every non-pass, builder refusal or error stores exactly one failure row and publishes nothing; a published plan stores one row naming it. A dry run
/// writes nothing at all. Time comes from the <see cref="TimeProvider"/>; the gates are the pure checks of <see cref="NightlyChecks"/>. Nothing here places an order or approves a ticket.
/// </summary>
internal sealed class RunNightlyPlanHandler(
    ITradingCalendar tradingCalendar,
    IEnumerable<INightlyGate> gates,
    IPlanBuilder builder,
    IPlanStore store,
    TimeProvider clock,
    ILogger<RunNightlyPlanHandler> logger) : ICommandHandler<RunNightlyPlanCommand, NightlyRunView>
{
    private const int HistoryRead = 200;

    public async ValueTask<Result<NightlyRunView>> HandleAsync(RunNightlyPlanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var started = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var session = command.Date ?? calendar.LastClosedSession(started);
        var forDate = NightlyRules.NextSession(calendar, session) ?? session.AddDays(1);

        var earlier = await store.ListRunsAsync(forDate, HistoryRead, cancellationToken);
        if (FirstPublished(earlier) is { } done)
        {
            NightlyLog.Repeat(logger, forDate, done.Run.PlanId ?? 0);
            return Result.Success(new NightlyRunView(session, forDate, NightlyOutcomes.Published, true, command.DryRun, done.Attempt, null, done.Run.Reason, done.Run.PlanId, []));
        }

        // A plan of the planned session counts as published even when no run row names it (a row lost by an older caller): the gates are not read again and no second plan is made.
        if (await store.ListAsync(forDate, null, 1, cancellationToken) is [var existing, ..])
        {
            NightlyLog.Repeat(logger, forDate, existing.Id);
            return Result.Success(new NightlyRunView(session, forDate, NightlyOutcomes.Published, true, command.DryRun, 0, null, $"A plan for {forDate:yyyy-MM-dd} is already stored (plan {existing.Id}); nothing was done.", existing.Id, []));
        }

        var attempt = earlier.Count + 1;
        var context = new GateContext(session, forDate, calendar, started);
        var read = new List<GateResult>();
        foreach (var gate in gates)
        {
            var result = await ReadAsync(gate, context, cancellationToken);
            read.Add(result);
            if (result.Blocks && !command.DryRun)
            {
                break;
            }
        }

        var views = read.Select(g => new NightlyGateView(g.Gate, g.Code, g.Reason)).ToList();
        if (read.FirstOrDefault(g => g.Blocks) is { } blocked)
        {
            return await FailAsync(command, context, attempt, blocked, null, views, started, cancellationToken);
        }

        var cutoff = NightlyRules.ReviewCutoff(forDate);
        NewPlan plan;
        try
        {
            var built = await builder.BuildAsync(new PlanBuildRequest(session, forDate, cutoff, started), cancellationToken);
            if (built.IsFailure)
            {
                return await FailAsync(command, context, attempt, GateResult.Failed(NightlyGateIds.Builder, $"The plan builder refused to build a plan ({built.Error.Code})."), null, views, started, cancellationToken);
            }

            plan = built.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a builder that throws must fail the run closed with a visible row, never publish
        catch (Exception ex)
#pragma warning restore CA1031
        {
            NightlyLog.GateError(logger, NightlyGateIds.Builder, ex.GetType().Name);
            return await FailAsync(command, context, attempt, GateResult.NotAvailable(NightlyGateIds.Builder, $"The plan builder failed ({ex.GetType().Name})."), null, views, started, cancellationToken);
        }

        if (command.DryRun)
        {
            NightlyLog.DryRun(logger, forDate, NightlyOutcomes.WouldPublish);
            return Result.Success(new NightlyRunView(session, forDate, NightlyOutcomes.WouldPublish, false, true, attempt, null, $"All gates passed; the plan would be published ({plan.Items.Count} tickets): {plan.Basis}.", null, views));
        }

        var publishReason = $"Published for {forDate:yyyy-MM-dd}: {plan.Basis}.";
        PlanPublished published;
        try
        {
            published = await store.PublishWithRunAsync(plan, new PublishRun(command.Manual, started, Later(started), Clip(publishReason)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the store is one transaction: nothing was published; the failure is recorded
        catch (Exception ex)
#pragma warning restore CA1031
        {
            NightlyLog.GateError(logger, NightlyGateIds.Publish, ex.GetType().Name);
            return await FailAsync(command, context, attempt, GateResult.NotAvailable(NightlyGateIds.Publish, $"The plan could not be stored ({ex.GetType().Name})."), plan.PlanKey, views, started, cancellationToken);
        }

        if (!published.Created && !published.RunRecorded)
        {
            // another run published the same plan between our check and our write: it has its row; ours is not written
            var winner = FirstPublished(await store.ListRunsAsync(forDate, HistoryRead, cancellationToken));
            NightlyLog.Repeat(logger, forDate, published.Id);
            return Result.Success(new NightlyRunView(session, forDate, NightlyOutcomes.Published, true, false, winner?.Attempt ?? 0, null, $"Already published by another run (plan {published.Id}); nothing was done.", published.Id, views));
        }

        NightlyLog.Published(logger, forDate, published.Id, attempt);
        return Result.Success(new NightlyRunView(session, forDate, NightlyOutcomes.Published, false, false, attempt, null, publishReason, published.Id, views));
    }

    private async ValueTask<Result<NightlyRunView>> FailAsync(RunNightlyPlanCommand command, GateContext c, int attempt, GateResult blocked, string? planKey, List<NightlyGateView> views, DateTimeOffset started, CancellationToken cancellationToken)
    {
        if (!command.DryRun)
        {
            await store.RecordRunAsync(new NewPlanRun(c.ForDate, planKey, NightlyOutcomes.FailedClosed, blocked.Gate, Clip(blocked.Reason), null, command.Manual, started, Later(started)), cancellationToken);
            NightlyLog.FailedClosed(logger, c.ForDate, blocked.Gate, blocked.Code, attempt);
        }
        else
        {
            NightlyLog.DryRun(logger, c.ForDate, NightlyOutcomes.FailedClosed);
        }

        return Result.Success(new NightlyRunView(c.Session, c.ForDate, NightlyOutcomes.FailedClosed, false, command.DryRun, attempt, blocked.Gate, blocked.Reason, null, views));
    }

    private async ValueTask<GateResult> ReadAsync(INightlyGate gate, GateContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await gate.CheckAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // an input that cannot be read is "unavailable", which blocks: fail closed
        catch (Exception ex)
#pragma warning restore CA1031
        {
            NightlyLog.GateError(logger, gate.Id, ex.GetType().Name);
            return GateResult.NotAvailable(gate.Id, $"The input of this gate could not be read ({ex.GetType().Name}).");
        }
    }

    /// <summary>The first published row of the session: the lowest id, the one a repeat reports.</summary>
    private static PlanRunRow? FirstPublished(IReadOnlyList<PlanRunRow> runs) =>
        runs.Where(r => r.Run.Outcome == NightlyOutcomes.Published).MinBy(r => r.Id);

    private DateTimeOffset Later(DateTimeOffset started) => clock.GetUtcNow() is var now && now >= started ? now : started;

    private static string Clip(string text) => text.Length <= GateResult.MaxReasonLength ? text : text[..(GateResult.MaxReasonLength - 1)] + "~";
}
