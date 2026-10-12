using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The aggregate phase (ADR 0016 §6): rebuilds the derived timeframes of every instrument whose 1-minute bars changed since its last build (for the buckets that
/// contain the changed range) — or of every instrument, when the job asked for nothing that writes bars — and refreshes the coverage. A job about one instrument (the
/// fix actions of the Data quality screen, ADR 0017 §4) builds only that instrument, and when it writes no bars rebuilds the instrument's quality window. After each instrument is built the
/// detection of corporate actions (ADR 0017 §5) looks at the days that changed and stores what it finds as suggestions; it never fails the phase.
/// </summary>
internal sealed class AggregatePhaseRunner(
    IBarAggregates aggregates,
    ITradingCalendar tradingCalendar,
    IDataSettings settings,
    CorporateActionDetection detection,
    TimeProvider clock,
    ILogger<AggregatePhaseRunner> logger)
{
    public async ValueTask<AggregatePhase> RunAsync(DataJob job, JobReporter reporter, CancellationToken cancellationToken)
    {
        var phase = AggregatePhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow(), Full = job.RebuildAll };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        if (job.RebuildAll && job.InstrumentId is { } instrumentId)
        {
            var sessions = QualityWindow.Sessions(await tradingCalendar.GetAsync(cancellationToken), clock.GetUtcNow(), settings.QualitySessions);
            if (sessions.Count > 0)
            {
                await aggregates.MarkDirtyAsync(instrumentId, DateTimeOffset.FromUnixTimeSeconds(MinuteAggregator.MidnightOf(sessions[0])), cancellationToken);
            }
        }
        else if (job.RebuildAll)
        {
            await aggregates.MarkAllDirtyAsync(cancellationToken);
        }

        var dirty = await aggregates.ListDirtyAsync(cancellationToken);
        if (job.InstrumentId is { } only)
        {
            dirty = [.. dirty.Where(d => d.InstrumentId == only)];
        }

        phase = phase with { Total = dirty.Count };
        if (dirty.Count == 0)
        {
            return phase with { Status = PhaseStatus.Succeeded, FinishedAt = clock.GetUtcNow(), Message = "Nothing changed since the last build." };
        }

        var byTimeframe = new Dictionary<string, long>(StringComparer.Ordinal);
        var suggested = 0;
        foreach (var target in dirty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reporter.Ended)
            {
                break;
            }

            var built = await aggregates.BuildAsync(target, cancellationToken);
            var written = 0L;
            foreach (var (timeframe, count) in built.BucketsByTimeframe)
            {
                byTimeframe[timeframe] = byTimeframe.GetValueOrDefault(timeframe) + count;
                written += count;
            }

            DataLog.AggregatesBuilt(logger, target.Symbol, written);
            suggested += await DetectAsync(target, cancellationToken);
            phase = phase with { Done = phase.Done + 1, Buckets = phase.Buckets + written, BucketsByTimeframe = new Dictionary<string, long>(byTimeframe) };
            await reporter.ReportAsync(phase, cancellationToken);
        }

        return phase with
        {
            Status = PhaseStatus.Succeeded,
            FinishedAt = clock.GetUtcNow(),
            Message = suggested == 0 ? phase.Message : suggested == 1 ? "1 corporate action suggested for review." : $"{suggested} corporate actions suggested for review.",
        };
    }

    // The days the build changed are the days the detection looks at. A failure is logged and skipped: the aggregates are what this phase is for.
    private async ValueTask<int> DetectAsync(DirtyInstrument target, CancellationToken cancellationToken)
    {
        try
        {
            var found = await detection.DetectAsync(target.InstrumentId, IndiaTime.Today(target.From), cancellationToken);
            if (found.Suggested > 0)
            {
                CorporateActionLog.Suggested(logger, target.Symbol, found.Suggested);
            }

            return found.Suggested;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // corporate action detection is a side effect of the aggregate build: whatever goes wrong must not fail the phase
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CorporateActionLog.DetectionFailed(logger, target.Symbol, ex);
            return 0;
        }
    }
}
