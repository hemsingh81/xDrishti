using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The aggregate phase (ADR 0016 §6): rebuilds the derived timeframes of every instrument whose 1-minute bars changed since its last build (for the buckets that
/// contain the changed range) — or of every instrument, when the job asked for nothing that writes bars — and refreshes the coverage.
/// </summary>
internal sealed class AggregatePhaseRunner(IBarAggregates aggregates, TimeProvider clock, ILogger<AggregatePhaseRunner> logger)
{
    public async ValueTask<AggregatePhase> RunAsync(DataJob job, JobReporter reporter, CancellationToken cancellationToken)
    {
        var phase = AggregatePhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow(), Full = job.RebuildAll };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        if (job.RebuildAll)
        {
            await aggregates.MarkAllDirtyAsync(cancellationToken);
        }

        var dirty = await aggregates.ListDirtyAsync(cancellationToken);
        phase = phase with { Total = dirty.Count };
        if (dirty.Count == 0)
        {
            return phase with { Status = PhaseStatus.Succeeded, FinishedAt = clock.GetUtcNow(), Message = "Nothing changed since the last build." };
        }

        var byTimeframe = new Dictionary<string, long>(StringComparer.Ordinal);
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
            phase = phase with { Done = phase.Done + 1, Buckets = phase.Buckets + written, BucketsByTimeframe = new Dictionary<string, long>(byTimeframe) };
            await reporter.ReportAsync(phase, cancellationToken);
        }

        return phase with { Status = PhaseStatus.Succeeded, FinishedAt = clock.GetUtcNow() };
    }
}
