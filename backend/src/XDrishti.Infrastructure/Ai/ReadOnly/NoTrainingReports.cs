using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>The read-only assistant host never reads training runs (ADR 0243, AU-14): its training source has no job and no trade, so a report there is the evidence report or nothing.</summary>
internal sealed class NoTrainingReports : ITrainingReportSource, ITrainingJobProgress
{
    public async IAsyncEnumerable<ReportTrade> StreamTradesAsync(Guid jobId, DateTimeOffset finishedBy, ReportTradeFilter filter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public ValueTask<IReadOnlyList<TrainingRunHeader>> ListRunsAsync(IReadOnlyList<string> runKeys, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<TrainingRunHeader>>([]);

    public ValueTask<SimAllSnapshot?> GetProgressAsync(Guid? id, CancellationToken cancellationToken) => ValueTask.FromResult<SimAllSnapshot?>(null);

    public ValueTask<IReadOnlyList<SimAllSnapshot>> ListProgressAsync(int skip, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<SimAllSnapshot>>([]);
}
