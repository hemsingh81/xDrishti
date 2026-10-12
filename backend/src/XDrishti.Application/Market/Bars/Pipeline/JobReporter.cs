using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// How a running phase tells the world where it is: records the phase in the job and writes the job to the database, at most twice a second unless
/// the caller insists (phase start and end), so progress is visible to polling requests without a write per file or instrument. A job that has been ended elsewhere
/// (marked failed as interrupted by the 12-hour rule or by recovery while this worker was still busy) is not written to again: the store reports that it is no longer
/// running, <see cref="Ended"/> turns true, and the phases stop at their next file or instrument.
/// </summary>
internal sealed class JobReporter(DataJob job, IDataJobStore store, TimeProvider clock)
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);

    private long _lastPublished;

    public DataJob Job => job;

    /// <summary>True once the store has said that the job is no longer running; nothing is written after that.</summary>
    public bool Ended { get; private set; }

    public async ValueTask ReportAsync(IDataPhase phase, CancellationToken cancellationToken, bool publishNow = false)
    {
        if (Ended)
        {
            return;
        }

        job.Report(phase);
        if (publishNow || clock.GetElapsedTime(_lastPublished) >= MinInterval)
        {
            await PublishAsync(cancellationToken);
        }
    }

    public async ValueTask WarnAsync(string warning, CancellationToken cancellationToken)
    {
        if (Ended)
        {
            return;
        }

        job.AddWarning(warning);
        await PublishAsync(cancellationToken);
    }

    public async ValueTask PublishAsync(CancellationToken cancellationToken)
    {
        if (Ended)
        {
            return;
        }

        Ended = !await store.PublishAsync(job, cancellationToken);
        _lastPublished = clock.GetTimestamp();
    }
}
