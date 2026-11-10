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
}
