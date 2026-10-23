using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
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
        var instruments = new Lazy<Task<Dictionary<string, Guid>>>(() => InstrumentsAsync(cancellationToken));
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

                var (grade, runKey, why) = await GradeAsync(plan.Plan.ForDate, lastClosed, calendar, item.Item, costs.Value, instruments, cancellationToken);
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
        DateOnly forDate, DateOnly lastClosed, TradingCalendar calendar, NewPlanItem item, CostChoice costs, Lazy<Task<Dictionary<string, Guid>>> instruments, CancellationToken cancellationToken)
    {
        if (CellId.TryParse(item.Evidence.CellKey) is not { } cell || (item.Side != "BUY" && item.Side != "SELL"))
        {
            return (TicketGrader.NotGradable("the ticket's cell key or side cannot be read"), null, null);
        }

        if (!ExitLabel.TryRead(cell.Exit, out var exit, out var exitReason))
        {
            return (TicketGrader.NotGradable(exitReason), null, null);
        }

        var stored = await strategies.GetByHashAsync(item.StrategyVersionHash, cancellationToken);
        if (stored is null)
        {
            return (TicketGrader.NotGradable("the strategy version of the ticket is not stored"), null, null);
        }

        // Shortcut: the symbol is mapped to an instrument through the non-archived baskets, first match wins (the plan item stores no instrument id). An instrument that has left every basket is pending, not final.
        if (!(await instruments.Value).TryGetValue(item.Symbol, out var instrumentId))
        {
            return (null, null, "the symbol `" + item.Symbol + "` is in no basket now, so its instrument is unknown");
        }

        // The engine cuts trades by the day of the DECISION. A ticket on a daily-or-longer timeframe is decided at the close of D (the session it was built on) and fills at the open of F, so its window starts on D; a ticket on an intraday
        // timeframe is decided on F, so its window starts on F. Only that one decision day is the ticket's (the grader checks). An intraday-style ticket is read to F, a swing ticket to the last closed session; nothing after the last closed
        // session is ever read. Only the counters the replayer cuts to its window (trades dropped by the sizing or cost rule, positions that cannot cross a discontinuity) are attributed to the decision day, by taking away a second run that starts the day after it (same end); flagged days and excluded sessions are read by date from the run record; no-trade reasons are not attributed.
        var daily = Timeframes.TryParse(cell.Timeframe, out var timeframe) && timeframe >= Timeframe.D1;
        var information = Before(calendar, forDate);
        var decisionDay = daily ? information : forDate;
        var swing = string.Equals(cell.Style, "swing", StringComparison.OrdinalIgnoreCase);
        var to = swing && lastClosed > forDate ? lastClosed : forDate;
        var after = decisionDay == information ? forDate : forDate.AddDays(1);
        var main = await simulation.HandleAsync(new RunSimulationQuery(Request(stored, cell, decisionDay, to, instrumentId, costs, exit)), cancellationToken);
        if (main.IsFailure)
        {
            // A refusal (a sheet not in force yet, a missing instrument row, a strategy that does not compile now) may be mended later: pending, not a final grade.
            return (null, null, "the simulator refused the ticket: " + Clip(main.Error.Code + " " + main.Error.Message));
        }

        SimulationRun? tail = null;
        if (after <= to)
        {
            var second = await simulation.HandleAsync(new RunSimulationQuery(Request(stored, cell, after, to, instrumentId, costs, exit)), cancellationToken);
            if (second.IsFailure)
            {
                return (null, null, "the simulator refused the ticket: " + Clip(second.Error.Code + " " + second.Error.Message));
            }

            tail = second.Value;
        }

        var result = TicketGrader.Grade(main.Value, tail, item.Side == "BUY" ? Side.Buy : Side.Sell, forDate, information, daily, swing);
        return (result.Grade, result.Grade is null ? null : main.Value.RunKey, result.PendingReason);
    }

    private static SimulationRequest Request(StoredVersion stored, CellId cell, DateOnly from, DateOnly to, Guid instrumentId, CostChoice costs, ExitChoice exit) =>
        new(stored.StrategyId, null, cell.Timeframe, from, to, [instrumentId], null, costs, StrategyHash: stored.Hash, Exits: exit);

    /// <summary>Gross when no sheet is configured; a configured sheet that cannot be found fails the run (no silent gross fallback).</summary>
    private async Task<Result<CostChoice>> CostsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CostSheetId))
        {
            return Result.Success(CostChoice.Gross);
        }

        var sheet = await costSheets.FindAsync(settings.CostSheetId, cancellationToken);
        if (sheet.IsFailure)
        {
            GradeLog.SheetMissing(logger, settings.CostSheetId);
            return Result.Failure<CostChoice>(PlanErrors.GradingSheetMissing(settings.CostSheetId));
        }

        return Result.Success(CostChoice.Sheet(sheet.Value.Id, settings.Capital));
    }

    /// <summary>
    /// The trading session before <paramref name="forDate"/> (the session the ticket was built on). Shortcut: when the calendar knows none within ten days (a long closure, or an empty calendar) the previous calendar day is used; the window
    /// then starts on a non-session and a daily decision at the close of the true D would be missed (the ticket reads as not triggered).
    /// </summary>
    private static DateOnly Before(TradingCalendar calendar, DateOnly forDate)
    {
        for (var i = 1; i <= 10; i++)
        {
            if (calendar.IsTradingDay(forDate.AddDays(-i)))
            {
                return forDate.AddDays(-i);
            }
        }

        return forDate.AddDays(-1);
    }

    private async Task<Dictionary<string, Guid>> InstrumentsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, null, cancellationToken) is { } members)
            {
                foreach (var m in members.Items)
                {
                    result.TryAdd(m.Instrument.Symbol, m.Instrument.Id);
                }
            }
        }

        return result;
    }

    private static string Clip(string text) => text.Length <= 300 ? text : string.Concat(text.AsSpan(0, 299), "…");
}
