using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Replay;

/// <summary>A stored text as the library and the verbs list it: id, style (<c>intraday</c>/<c>swing</c>), the content hash and canonical text, the timeframes, the sides and the declared parameters with their values.</summary>
public sealed record StoredDescription(
    string Id,
    string Style,
    string Hash,
    string CanonicalJson,
    IReadOnlyList<Timeframe> Timeframes,
    IReadOnlyList<Side> Sides,
    IReadOnlyList<KeyValuePair<string, double>> Parameters);
