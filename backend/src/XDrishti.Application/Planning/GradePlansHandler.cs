using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Planning;

/// <summary>
/// Grades the tickets of past plans (spec P7-13, ADR 0090). For each ticket without a grade it asks the simulation use case for exactly the planned session (<c>From = To = F</c>; a swing ticket is read to the last closed session)
/// with the ticket's stored strategy version, its cell's exit and the configured charge sheet, and turns the record into a grade with the pure <see cref="TicketGrader"/>. Pending tickets are not written; the rest are inserted once.
/// It reads and inserts grades only: no ticket state changes, no order, no approval.
/// </summary>
internal sealed class GradePlansHandler(
    IPlanStore plans,
    IGradeStore grades,
    IGradeSettings settings,
    IStrategyStore strategies,
    IBasketQueries baskets,
    ICostSheetStore costSheets,
    ITradingCalendar calendars,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    TimeProvider time,
    ILogger<GradePlansHandler> logger) : ICommandHandler<GradePlansCommand, GradeOutcome>
{
    private const int MaxPlans = 30;

    private readonly TicketReplayer _replayer = new(strategies, baskets, simulation);

    public async ValueTask<Result<GradeOutcome>> HandleAsync(GradePlansCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (command.Date is { } d && d >= today)
        {
            return Result.Failure<GradeOutcome>(PlanErrors.Invalid([new FieldError("date", "A session can be graded only after it has been; the date must be before today.")]));
        }

        var rows = await plans.ListAsync(command.Date, null, MaxPlans, cancellationToken);
        var due = rows.Where(r => r.Plan.ForDate < today).OrderBy(r => r.Plan.ForDate).ThenBy(r => r.Id).ToList();
        var costs = await CostsAsync(cancellationToken);
        if (costs.IsFailure)
        {
            return Result.Failure<GradeOutcome>(costs.Error);
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(now);
        int graded = 0, notTriggered = 0, notGradable = 0, pending = 0, already = 0;
        var stored = await grades.ForPlansAsync([.. due.Select(p => p.Id)], cancellationToken); // one read for every due plan (ticket ids are unique across plans)
        foreach (var plan in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await plans.ItemsAsync(plan.Id, cancellationToken);
            var existing = stored;
            var batch = new List<NewGrade>();
            foreach (var item in items)
            {
                if (existing.ContainsKey(item.Id))
                {
                    already++;
                    continue;
                }

                var (grade, runKey, why) = await GradeAsync(plan.Plan.ForDate, lastClosed, calendar, item.Item, costs.Value, cancellationToken);
                if (grade is null)
                {
                    pending++;
                    GradeLog.Pending(logger, plan.Id, item.Id, why ?? string.Empty);
                    continue;
                }

                batch.Add(new NewGrade(plan.Id, item.Id, plan.Plan.ForDate, grade, runKey, now));
            }

            if (batch.Count > 0)
            {
                await grades.InsertAsync(batch, cancellationToken);
                graded += batch.Count(b => b.Grade.Status == GradeStatus.Graded);
                notTriggered += batch.Count(b => b.Grade.Status == GradeStatus.NotTriggered);
                notGradable += batch.Count(b => b.Grade.Status == GradeStatus.NotGradable);
            }
        }

        GradeLog.Done(logger, due.Count, graded, notTriggered, notGradable, pending);
        return Result.Success(new GradeOutcome(due.Count, graded, notTriggered, notGradable, pending, already));
    }

    private async Task<(TicketGrade? Grade, string? RunKey, string? Pending)> GradeAsync(
        DateOnly forDate, DateOnly lastClosed, TradingCalendar calendar, NewPlanItem item, CostChoice costs, CancellationToken cancellationToken)
    {
        var replay = await _replayer.ReplayAsync(forDate, lastClosed, calendar, item, costs, cancellationToken);
        return (replay.Grade, replay.Grade is null ? null : replay.RunKey, replay.Pending);
    }

    /// <summary>Gross when no sheet is configured; a configured sheet that cannot be found fails the run (no silent gross fallback).</summary>
    private async Task<Result<CostChoice>> CostsAsync(CancellationToken cancellationToken)
    {
        var costs = await ReplayCosts.ResolveAsync(settings, costSheets, cancellationToken);
        if (costs.IsFailure)
        {
            GradeLog.SheetMissing(logger, settings.CostSheetId ?? string.Empty);
            return Result.Failure<CostChoice>(PlanErrors.GradingSheetMissing(settings.CostSheetId ?? string.Empty));
        }

        return Result.Success(costs.Value.Choice);
    }
}
