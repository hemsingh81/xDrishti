using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// The quality phase of a data job (ADR 0017 §4, P1-14a): runs the pure checks (<see cref="QualityEngine"/>) over the last <c>Data:Quality:Sessions</c> closed trading sessions of every tracked instrument
/// with bars — or of the one instrument the job is about — and records the run and one result per instrument. It reads each instrument's 1-minute bars of the window (by the primary key), its stored
/// aggregates and its gap records, and writes nothing else. Results are saved in small batches, so progress is visible and an interrupted run keeps what it did. Never fails the job because instruments fail
/// their checks: that is its result. An instrument whose checks crash is recorded as failed, with the reason in words.
/// </summary>
internal sealed class QualityPhaseRunner(
    IQualityData data,
    IQualityRuns runs,
    IBarStore bars,
    IBarGaps gaps,
    ITradingCalendar tradingCalendar,
    IDataSettings settings,
    TimeProvider clock,
    ILogger<QualityPhaseRunner> logger)
{
    /// <summary>How many results are written together.</summary>
    private const int BatchSize = 20;

    public async ValueTask<QualityPhase> RunAsync(DataJob job, JobReporter reporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(reporter);
        var phase = QualityPhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow() };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        var now = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var sessions = QualityWindow.Sessions(calendar, now, settings.QualitySessions);
        if (sessions.Count == 0)
        {
            return End(phase, PhaseStatus.Skipped, null, "The trading calendar has no closed session to check.");
        }

        var targets = await data.ListTargetsAsync(job.InstrumentId, cancellationToken);
        if (targets.Count == 0)
        {
            return End(phase, PhaseStatus.Skipped, null, job.InstrumentId is null ? "No tracked instrument has bars: nothing to check." : "The instrument is not tracked or has no bars: nothing to check.");
        }

        var run = QualityRun.Start(TriggerOf(job), sessions.Count, targets.Count, job.Id, job.InstrumentId, now);
        if (!await runs.TryStartAsync(run, cancellationToken))
        {
            return End(phase, PhaseStatus.Failed, DataErrors.JobRunningCode, "Another data quality run is in progress.");
        }

        var trigger = run.Trigger.ToCode();
        QualityLog.RunStarted(logger, run.Id, trigger, targets.Count, sessions.Count);
        phase = phase with { RunId = run.Id, Total = targets.Count };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        try
        {
            phase = await CheckAllAsync(phase, run, targets, sessions, calendar, reporter, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailRunAsync(run, "The quality run was interrupted because the application is stopping.");
            throw;
        }
#pragma warning disable CA1031 // any failure must end the run record as failed before it is rethrown to the job
        catch (Exception ex)
#pragma warning restore CA1031
        {
            QualityLog.RunCrashed(logger, run.Id, ex);
            await FailRunAsync(run, "The quality run failed unexpectedly. See the API log.");
            throw;
        }

        return phase;
    }

    private async ValueTask<QualityPhase> CheckAllAsync(
        QualityPhase phase,
        QualityRun run,
        IReadOnlyList<QualityTarget> targets,
        IReadOnlyList<DateOnly> sessions,
        Domain.Market.Calendar.TradingCalendar calendar,
        JobReporter reporter,
        CancellationToken cancellationToken)
    {
        var thresholds = new QualityThresholds(settings.QualitySpikeMultiple, settings.QualityMaxMissingPercent);
        var bounds = QualityWindow.Bounds(sessions[0]);
        var batch = new List<QualityResult>(BatchSize);
        var scores = new List<int>(targets.Count);
        var now = clock.GetUtcNow();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reporter.Ended)
            {
                break;
            }

            var outcome = await CheckAsync(run, target, sessions, calendar, bounds, thresholds, now, cancellationToken);
            batch.Add(QualityResult.From(run.Id, target.InstrumentId, outcome));
            scores.Add(outcome.Score);
            run.Record(outcome.Status);
            phase = Count(phase, outcome.Status);

            if (batch.Count >= BatchSize)
            {
                await FlushAsync(run, batch, cancellationToken);
            }

            await reporter.ReportAsync(phase, cancellationToken);
        }

        await FlushAsync(run, batch, cancellationToken);
        if (reporter.Ended)
        {
            // The job was ended elsewhere (failed as interrupted): so is the run.
            await FailRunAsync(run, "The data update was ended while the quality run was busy.");
            return phase with { Status = PhaseStatus.Failed, FinishedAt = clock.GetUtcNow(), Message = "The data update was ended while the quality run was busy." };
        }

        run.Finish(clock.GetUtcNow(), scores.Count == 0 ? null : scores.Average());
        await runs.PublishAsync(run, CancellationToken.None);
        QualityLog.RunFinished(logger, run.Id, run.Passed, run.Warned, run.Failed);
        return phase with
        {
            Status = PhaseStatus.Succeeded,
            FinishedAt = clock.GetUtcNow(),
            Message = $"{phase.Passed} passed, {phase.Warned} with warnings, {phase.Failed} failed.",
        };
    }

    private async ValueTask<QualityOutcome> CheckAsync(
        QualityRun run,
        QualityTarget target,
        IReadOnlyList<DateOnly> sessions,
        Domain.Market.Calendar.TradingCalendar calendar,
        QualityBounds bounds,
        QualityThresholds thresholds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var minuteBars = await bars.ReadAsync(target.InstrumentId, DateTimeOffset.FromUnixTimeSeconds(bounds.ReadFrom), now.AddDays(2), cancellationToken);
            var stored = await data.ReadStoredBucketsAsync(target.InstrumentId, bounds, cancellationToken);
            var unfillable = (await gaps.ListForAsync(target.InstrumentId, sessions[0], cancellationToken))
                .Where(g => g.Status == GapStatus.Unfillable && g.Date >= sessions[0])
                .Select(g => g.Date)
                .ToHashSet();
            return QualityEngine.Evaluate(new QualityInput(now, calendar, sessions, target.FirstBar, target.LastBar, minuteBars, stored, unfillable), thresholds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one instrument whose checks crash must not stop the others: it is recorded as failed (fail-closed) and logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            QualityLog.InstrumentCrashed(logger, run.Id, target.Symbol, ex);
            return Crashed(target, calendar, now);
        }
    }

    /// <summary>The result of an instrument whose checks could not run: failed, score 0, every check failed with nothing known (the reason is in the log).</summary>
    private static QualityOutcome Crashed(QualityTarget target, Domain.Market.Calendar.TradingCalendar calendar, DateTimeOffset now)
    {
        var checks = new QualityChecks(
            new CompletenessCheck(CheckStatus.Fail, 0, 0, 0, 0, 0, []),
            new SanityCheck(CheckStatus.Fail, 0, 0, []),
            new SpikeCheck(CheckStatus.Fail, 0, 0, 0, 0, []),
            new AggregatesCheck(CheckStatus.Fail, 0, 0, new Dictionary<string, TimeframeCheck>()),
            FreshnessChecker.Check(calendar, now, target.LastBar) with { Status = CheckStatus.Fail },
            OfficialDailyCheck.NotAvailable,
            new QualityDetails([], []));
        return new QualityOutcome(CheckStatus.Fail, 0, 0, 0, checks);
    }

    private async ValueTask FlushAsync(QualityRun run, List<QualityResult> batch, CancellationToken cancellationToken)
    {
        if (batch.Count > 0)
        {
            await runs.SaveResultsAsync(batch, cancellationToken);
            batch.Clear();
        }

        await runs.PublishAsync(run, cancellationToken);
    }

    private async ValueTask FailRunAsync(QualityRun run, string error)
    {
        if (run.IsRunning)
        {
            run.Fail(clock.GetUtcNow(), error);
            await runs.PublishAsync(run, CancellationToken.None);
        }
    }

    private static QualityPhase Count(QualityPhase phase, string status)
    {
        var done = phase with { Done = phase.Done + 1 };
        return status switch
        {
            CheckStatus.Pass => done with { Passed = done.Passed + 1 },
            CheckStatus.Warning => done with { Warned = done.Warned + 1 },
            _ => done with { Failed = done.Failed + 1 },
        };
    }

    /// <summary>The trigger the run is recorded with: the scheduled run is <c>daily</c>; a job the owner started that also fetched or aggregated is <c>after_job</c>; a job of the check alone is <c>manual</c>.</summary>
    private static QualityTrigger TriggerOf(DataJob job) =>
        job.Trigger == DataJobTrigger.Daily ? QualityTrigger.Daily
        : job.Steps.Any(step => step != DataSteps.Quality) ? QualityTrigger.AfterJob
        : QualityTrigger.Manual;

    private QualityPhase End(QualityPhase phase, string status, string? code, string message) =>
        phase with { Status = status, Code = code, Message = message, FinishedAt = clock.GetUtcNow() };
}
