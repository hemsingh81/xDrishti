using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.SimAll;

/// <summary>A stored training run as the learning job may see it.</summary>
public sealed record TrainingRunHeader(string RunKey, string StrategyId, string DefinitionSha256, DateOnly From, DateOnly To, int Instruments, int InstrumentsRun, int TradeCount, int OpenAtEndCount, DateTimeOffset CreatedAt);

/// <summary>
/// The one reader of <c>training</c> runs (spec AU-12, ADR 0243 point 7, ADR 0248). It is explicitly NOT evidence: nothing in the Lab, the scorecard, cell lifecycle, plan selection or model promotion may use it. The architecture tests pin the namespaces
/// that may name it. A reader of evidence reads through the run store, which leaves training runs out.
/// </summary>
public interface ITrainingRunReader
{
    /// <summary>The headers of the training runs of a strategy definition, oldest first, at most <paramref name="take"/>.</summary>
    ValueTask<IReadOnlyList<TrainingRunHeader>> ListAsync(string definitionSha256, int take, CancellationToken cancellationToken);

    /// <summary>The header of a training run by key, or null (also null for a research run: this reader does not see them).</summary>
    ValueTask<TrainingRunHeader?> FindAsync(string runKey, CancellationToken cancellationToken);

    /// <summary>The record text of a training run (null when there is none); the feature extraction reads which instruments the run admitted pre-seam bars for (CF-04d). The default answers null: no instrument admitted.</summary>
    ValueTask<string?> ReadRecordTextAsync(string runKey, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    /// <summary>The closed trades of a training run in stored order (cash trades; none for a research run or an unknown key). Memory-bounded: streamed, one row at a time.</summary>
    IAsyncEnumerable<RunTrade> StreamTradesAsync(string runKey, CancellationToken cancellationToken);

    /// <summary>The headers of the training runs with these keys (at most 100,000 keys; a key that is not a training run gives no header), in the order of the keys asked (AU-14).</summary>
    ValueTask<IReadOnlyList<TrainingRunHeader>> ListByKeysAsync(IReadOnlyList<string> runKeys, CancellationToken cancellationToken);

    /// <summary>
    /// The closed trades, in the report shape, of the runs of the chunks of one job that were done by <paramref name="finishedBy"/> and pass the filter (AU-14): a streamed projection, one row at a time, in the order of run then ordinal.
    /// The filter is applied in the database: symbols narrow the chunks by instrument first, baskets are the baskets stored on the trade.
    /// </summary>
    IAsyncEnumerable<ReportTrade> StreamJobTradesAsync(Guid jobId, DateTimeOffset finishedBy, ReportTradeFilter filter, CancellationToken cancellationToken);
}
