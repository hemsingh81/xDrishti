namespace XDrishti.Application.Reporting;

/// <summary>A stored run a report may use, with whether the default run set takes it and, if not, why.</summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="UniverseKind">How the universe was chosen (instrumentlist or basket).</param>
/// <param name="UniverseLabel">What the universe is, as stored.</param>
/// <param name="WindowStart">First India date of the run window.</param>
/// <param name="WindowEnd">Last India date of the run window.</param>
/// <param name="Costed">The run carries net R.</param>
/// <param name="CostsSheetId">The charge sheet id; null for a gross run.</param>
/// <param name="Style">The style of the stored strategy; null when not recorded.</param>
/// <param name="StyleSource">Where the style comes from; null when not recorded.</param>
/// <param name="Exit">The exit candidate of the stored comparison the run belongs to; null when not recorded.</param>
/// <param name="ExitSource">Where the exit comes from; null when not recorded.</param>
/// <param name="ClosedTrades">Closed trades the run holds.</param>
/// <param name="OpenAtEnd">Trades still open at the end of the data, never counted.</param>
/// <param name="NetPossible">A net report over this run is possible: it was costed with the very charge sheet, with the very rates, the owner confirmed.</param>
/// <param name="InDefaultSet">The default run set takes this run.</param>
/// <param name="DefaultSetNote">Why the default set leaves the run out; null when it takes it.</param>
/// <param name="CreatedAt">When the run was stored.</param>
public sealed record RunListItemView(
    string RunKey,
    string StrategyId,
    string UniverseKind,
    string UniverseLabel,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    bool Costed,
    string? CostsSheetId,
    string? Style,
    string? StyleSource,
    string? Exit,
    string? ExitSource,
    int ClosedTrades,
    int OpenAtEnd,
    bool NetPossible,
    bool InDefaultSet,
    string? DefaultSetNote,
    DateTimeOffset CreatedAt);
