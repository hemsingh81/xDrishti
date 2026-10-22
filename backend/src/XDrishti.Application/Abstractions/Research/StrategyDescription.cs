using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>A strategy text that the engine has read and compiled (spec P3-02d, D8): what the store and the verbs need to know about it. Plain values only.</summary>
public sealed record StrategyDescription(
    string Id,
    string Style,
    string Hash,
    string CanonicalJson,
    IReadOnlyList<Timeframe> Timeframes,
    IReadOnlyList<Side> Sides,
    IReadOnlyList<KeyValuePair<string, double>> Parameters);
