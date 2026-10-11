using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Application.Market.Import;

/// <summary>The master list a refresh will write, with the figures and warnings found while building it.</summary>
internal sealed record CatalogBuild(
    IReadOnlyList<InstrumentCandidate> Candidates,
    int Expired,
    int Collisions,
    int SectorMatched,
    int SectorUnmatched,
    int FnoUnderlyings,
    IReadOnlyDictionary<string, int> KeptByType,
    IReadOnlyList<string> Warnings);
