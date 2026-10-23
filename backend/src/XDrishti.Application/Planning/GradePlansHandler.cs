using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
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
        int graded = 0, notTriggered = 0, notGradable = 0, pending = 0, already = 0;
        foreach (var plan in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await plans.ItemsAsync(plan.Id, cancellationToken);
            var existing = await grades.ForPlansAsync([plan.Id], cancellationToken);
            var batch = new List<NewGrade>();
            foreach (var item in items)
            {
                if (existing.ContainsKey(item.Id))
                {
                    already++;
                    continue;
                }

                var (grade, runKey, why) = await GradeAsync(plan.Plan.ForDate, today, item.Item, costs, instruments, cancellationToken);
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
        DateOnly forDate, DateOnly today, NewPlanItem item, CostChoice costs, Lazy<Task<Dictionary<string, Guid>>> instruments, CancellationToken cancellationToken)
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

        if (!(await instruments.Value).TryGetValue(item.Symbol, out var instrumentId))
        {
            return (TicketGrader.NotGradable("the symbol `" + item.Symbol + "` is in no basket, so its instrument is unknown"), null, null);
        }

        var swing = string.Equals(cell.Style, "swing", StringComparison.OrdinalIgnoreCase);
        var last = today.AddDays(-1);
        var to = swing && last > forDate ? last : forDate;
        var request = new SimulationRequest(stored.StrategyId, null, cell.Timeframe, forDate, to, [instrumentId], null, costs, StrategyHash: stored.Hash, Exits: exit);
        var run = await simulation.HandleAsync(new RunSimulationQuery(request), cancellationToken);
        if (run.IsFailure)
        {
            return (TicketGrader.NotGradable("the simulator refused the ticket: " + Clip(run.Error.Code + " " + run.Error.Message)), null, null);
        }

        var result = TicketGrader.Grade(run.Value, item.Side == "BUY" ? Side.Buy : Side.Sell, forDate);
        return (result.Grade, result.Grade is null ? null : run.Value.RunKey, result.PendingReason);
    }

    private async Task<CostChoice> CostsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CostSheetId))
        {
            return CostChoice.Gross;
        }

        var sheet = await costSheets.FindAsync(settings.CostSheetId, cancellationToken);
        return sheet.IsSuccess ? CostChoice.Sheet(sheet.Value.Id, settings.Capital) : CostChoice.Gross;
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
