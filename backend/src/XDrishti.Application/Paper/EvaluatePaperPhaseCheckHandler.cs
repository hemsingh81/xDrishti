using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Application.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Paper;

/// <summary>
/// The paper phase check (spec P10-06, ADR 0123). It composes reads only: the epoch and its history, the stored plan runs and plans, the capture rows, the counted trades, the backtest comparison (P10-04), the discipline lines (P10-03), the P7 phase check
/// and the services and settings, and hands plain facts to the pure <see cref="PaperPhaseCheck"/>. Nothing is written, no replay is started and no setting is changed; a part that cannot be read is named in the report, never a quiet MET.
/// </summary>
internal sealed class EvaluatePaperPhaseCheckHandler(
    ITradingCalendar tradingCalendar,
    IPaperLedgerData ledger,
    IBacktestPaperData paperData,
    IPaperPhaseData phaseData,
    IGovernanceStore governance,
    IPlanStore plans,
    IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>> comparison,
    IQueryHandler<GetDisciplineQuery, Result<DisciplineView>> discipline,
    IQueryHandler<EvaluatePlanPhaseCheckQuery, Result<PlanPhaseCheckView>> planCheck,
    IServiceScheduleReader services,
    IOpenBookSettings openBook,
    IAutonomySettingsStore autonomy,
    TimeProvider clock,
    ILogger<EvaluatePaperPhaseCheckHandler> logger) : IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>>
{
    private const int RunRowsRead = 5_000;
    private const int PlansRead = 5_000;
    private const int ChangesRead = 500;

    public async ValueTask<Result<PaperPhaseCheckView>> HandleAsync(EvaluatePaperPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.EpochId is <= 0)
        {
            return Refuse(PaperErrors.Invalid("epoch", "The epoch id must be positive."));
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(clock.GetUtcNow());
        var epoch = await ledger.EpochAsync(query.EpochId, cancellationToken);
        if (epoch is null && query.EpochId is not null)
        {
            return Refuse(PaperErrors.NotFound("paper epoch with this id"));
        }

        var through = lastClosed;
        if (query.Through is { } asked)
        {
            if (OnOrBefore(calendar, asked) is not { } rounded)
            {
                return Refuse(PaperErrors.Invalid("through", "No trading session lies on or before this date."));
            }

            if (rounded > lastClosed)
            {
                return Refuse(PaperErrors.Invalid("through", string.Create(CultureInfo.InvariantCulture, $"The newest closed session is {lastClosed:yyyy-MM-dd}; a later date has nothing to read.")));
            }

            through = rounded;
            if (epoch is not null && through < epoch.StartedOn)
            {
                return Refuse(PaperErrors.Invalid("through", string.Create(CultureInfo.InvariantCulture, $"The epoch starts on {epoch.StartedOn:yyyy-MM-dd}; an earlier date has nothing to count.")));
            }
        }

        var history = await governance.ListEpochsAsync(cancellationToken);
        var historyFacts = history.OrderBy(h => h.Epoch.Id).Select(h => new PhaseEpochFact(h.Epoch.Id, h.Epoch.StartedOn, h.Epoch.Kind, h.Epoch.Reason, h.Counted, h.UnexplainedChanges, h.RiskDownChanges)).ToList();
        var setup = await SetupAsync(cancellationToken);
        var p7 = await P7Async(through, cancellationToken);

        if (epoch is null)
        {
            var none = new PaperPhaseFacts(null, historyFacts, through, calendar.IsTradingDay(through), [], [], null, [], p7.Verdict, p7.Label, ["unavailable: no paper epoch yet"], setup.Services, setup.Switches);
            return Done(none, 0);
        }

        var data = await paperData.ReadAsync(epoch.Id, cancellationToken);
        if (data.NextEpochStart is { } next)
        {
            var end = OnOrBefore(calendar, next.AddDays(-1)) ?? epoch.StartedOn;
            if (query.Through is not null && through > end)
            {
                return Refuse(PaperErrors.Invalid("through", string.Create(CultureInfo.InvariantCulture, $"This epoch ended before {next:yyyy-MM-dd}; the last session it can count is {end:yyyy-MM-dd}.")));
            }

            through = through > end ? end : through;
        }

        var counted = CountedSet.Select(data.Rows, data.OtherEpochs).Trades;
        var sessions = await SessionsAsync(calendar, epoch.StartedOn, through, cancellationToken);
        var exit = await ExitAsync(epoch.Id, cancellationToken);
        var changes = await ChangesAsync(epoch.Id, cancellationToken);
        var lines = await DisciplineAsync(epoch, through, cancellationToken);
        var facts = new PaperPhaseFacts(
            historyFacts.Find(h => h.Id == epoch.Id) ?? new PhaseEpochFact(epoch.Id, epoch.StartedOn, epoch.Kind, epoch.Reason, counted.Count, 0, 0),
            historyFacts, through, calendar.IsTradingDay(through), sessions, counted, exit, changes, p7.Verdict, p7.Label, lines, setup.Services, setup.Switches);
        return Done(facts, epoch.Id);
    }

    private Result<PaperPhaseCheckView> Refuse(Error error)
    {
        PaperPhaseLog.Refused(logger, error.Code);
        return Result.Failure<PaperPhaseCheckView>(error);
    }

    private Result<PaperPhaseCheckView> Done(PaperPhaseFacts facts, long epochId)
    {
        var report = PaperPhaseCheck.Evaluate(facts);
        var v = report.Criteria.Select(c => c.Verdict).ToArray();
        PaperPhaseLog.Evaluated(logger, epochId, report.Through, report.ExitVerdict, v[0], v[1], v[2], v[3], v[4], facts.Counted.Count, report.ElapsedDays);
        if (report.ExitCriterion.Contains("malformed", StringComparison.Ordinal))
        {
            PaperPhaseLog.Malformed(logger, epochId);
        }

        return Result.Success(new PaperPhaseCheckView(report, PaperPhaseText.Render(report)));
    }

    private static DateOnly? OnOrBefore(TradingCalendar calendar, DateOnly date)
    {
        for (var i = 0; i < 30; i++)
        {
            var d = date.AddDays(-i);
            if (calendar.IsTradingDay(d))
            {
                return d;
            }
        }

        return null;
    }

    private async ValueTask<List<PhaseSessionFact>> SessionsAsync(TradingCalendar calendar, DateOnly from, DateOnly through, CancellationToken cancellationToken)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d <= through; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                dates.Add(d);
            }
        }

        if (dates.Count == 0)
        {
            return [];
        }

        var runs = (await plans.ListRunsAsync(null, RunRowsRead, cancellationToken)).Where(r => r.Run.ForDate >= from && r.Run.ForDate <= through).ToLookup(r => r.Run.ForDate);
        var planRows = (await plans.ListAsync(null, null, PlansRead, cancellationToken)).ToDictionary(p => p.Id);
        var captures = (await phaseData.CaptureRunsAsync(from, through, cancellationToken)).ToLookup(r => r.SessionDate);
        var result = new List<PhaseSessionFact>();
        foreach (var date in dates)
        {
            var nights = runs[date].OrderBy(r => r.Id).ToList();
            var published = nights.Find(r => r.Run.Outcome == "published");
            int? primary = published?.Run.PlanId is { } planId && planRows.TryGetValue(planId, out var row) ? row.Primary : null;
            var rows = captures[date].OrderBy(r => r.Id).ToList();
            var newest = rows.Count == 0 ? null : rows[^1];
            result.Add(new PhaseSessionFact(
                date, published is not null, nights.Count(r => r.Run.Outcome == "failed_closed"), nights.Count(r => r.Run.Manual), primary, rows.Count,
                newest is { Outcome: "done", Pending: 0 }, newest is { Pending: > 0 }, rows.Sum(r => r.Integrity)));
        }

        return result;
    }

    private async ValueTask<PhaseExitFacts?> ExitAsync(long epochId, CancellationToken cancellationToken)
    {
        var result = await comparison.HandleAsync(new GetBacktestVsPaperQuery(epochId, null), cancellationToken);
        if (result.IsFailure)
        {
            PaperPhaseLog.PartUnavailable(logger, "backtest comparison", result.Error.Code);
            return null;
        }

        var v = result.Value;
        var s = v.SensitivityAllProposals;
        return new PhaseExitFacts(
            v.EpochId,
            new CriterionResult(v.Criteria.Expectancy.Verdict, v.Criteria.Expectancy.Label),
            new CriterionResult(v.Criteria.Drawdown.Verdict, v.Criteria.Drawdown.Label),
            v.Paper.Ci90.Lo.Number,
            v.Paper.Ci90.Hi.Number,
            v.EarlyWarning,
            string.Create(CultureInfo.InvariantCulture, $"n {s.N}, mean {Show(s.Mean)} ({s.Label})"),
            v.ChargeSheet.ConfirmedOn is not null);
    }

    private async ValueTask<List<PhaseChangeFact>> ChangesAsync(long epochId, CancellationToken cancellationToken)
    {
        try
        {
            return [.. (await governance.ListChangesAsync(epochId, ChangesRead, cancellationToken)).Select(c => new PhaseChangeFact(c.SessionDate, c.Class))];
        }
#pragma warning disable CA1031 // The week lines show no change counts when the changes cannot be read; the criteria do not depend on them.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            PaperPhaseLog.PartUnavailable(logger, "configuration changes", failure.GetType().Name);
            return [];
        }
    }

    private async ValueTask<List<string>> DisciplineAsync(EpochRow epoch, DateOnly through, CancellationToken cancellationToken)
    {
        var result = await discipline.HandleAsync(new GetDisciplineQuery(epoch.Id, epoch.StartedOn, through), cancellationToken);
        if (result.IsFailure)
        {
            PaperPhaseLog.PartUnavailable(logger, "discipline report", result.Error.Code);
            return ["unavailable: " + result.Error.Code];
        }

        var v = result.Value;
        return v.Unavailable is not null
            ? ["unavailable: " + v.Unavailable]
            : [.. v.Lines.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Title}: n {l.N}, R {Show(l.RMetric)}"))];
    }

    private async ValueTask<(string? Verdict, string Label)> P7Async(DateOnly through, CancellationToken cancellationToken)
    {
        var result = await planCheck.HandleAsync(new EvaluatePlanPhaseCheckQuery(through), cancellationToken);
        if (result.IsFailure)
        {
            PaperPhaseLog.PartUnavailable(logger, "P7 phase check", result.Error.Code);
            return (null, result.Error.Code);
        }

        return (result.Value.Report.ExitVerdict, result.Value.Report.ExitCriterion);
    }

    private async ValueTask<(List<PhaseService> Services, List<PhaseSwitch> Switches)> SetupAsync(CancellationToken cancellationToken)
    {
        var definitions = new (string Id, string Role, bool Required)[]
        {
            (PaperCaptureService.Id, "the paper capture after the end-of-day run", true),
            (PaperReviewService.Id, "the weekly paper review", false),
            (NightlyService.Id, "the nightly plan", true),
            (PreOpenService.Id, "the pre-open check and arming", true),
            (PlanExpiryService.Id, "the expiry of tickets", true),
            (WeeklyService.Id, "the weekly learning digest", false),
        };
        var states = new List<PhaseService>();
        foreach (var (id, role, required) in definitions)
        {
            var info = await services.GetAsync(id, cancellationToken);
            states.Add(new PhaseService(id, role, required, info is null ? "not registered" : info.Enabled ? "enabled" : "paused"));
        }

        var current = await autonomy.CurrentAsync(cancellationToken);
        List<PhaseSwitch> switches =
        [
            new("Planning:OpenBook:UsePaperLedger", openBook.UsePaperLedger, "read by this process: the plan reads the open book from the paper ledger; OFF (the default) leaves the book as before; not part of the exit", false),
            new(AutonomySettings.AutoApproveKey, current.AutoApproveTickets, "auto-approval before the cutoff; OFF (the default) means the owner approves by hand; trades it approves are counted and labelled", false),
        ];
        return (states, switches);
    }

    private static string Show(MetricView m) =>
        m.Number is { } n
            ? n.ToString("0.000", CultureInfo.InvariantCulture) + (m.State == "provisional" ? " (provisional)" : string.Empty)
            : m.State + (m.Reason is null ? string.Empty : " (" + m.Reason + ")");
}
