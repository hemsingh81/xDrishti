namespace XDrishti.Domain.Market;

/// <summary>What a successful refresh did (the counts shown to the owner).</summary>
public sealed record RefreshOutcome(
    int Added,
    int Updated,
    int Retired,
    int Revived,
    int Unchanged,
    int SkippedByScope,
    int Collisions,
    int SectorMatched,
    int SectorUnmatched,
    int FnoUnderlyings,
    int TotalActive,
    long RowsRead,
    bool SectorsApplied,
    IReadOnlyDictionary<string, int> KeptByType,
    IReadOnlyList<string> Warnings);
