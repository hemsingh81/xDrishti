using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// The strategy as a run records it (spec P3-04 Scope 2): the id, the parameters the definition was built with (effective values, in ordinal name order; the replay does not interpret them), the canonical JSON of the definition
/// (interim, until P3-02c defines the canonical text) and its SHA-256, the evaluation timeframe and the sides. The text is the version (ADR 0002): two spellings of one feature give two hashes.
/// </summary>
public sealed record StrategyRecord(
    string Id,
    IReadOnlyDictionary<string, double> Parameters,
    string DefinitionJson,
    string DefinitionSha256,
    Timeframe EvaluationTimeframe,
    IReadOnlyList<Side> Sides);
