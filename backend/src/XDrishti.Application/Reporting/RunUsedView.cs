namespace XDrishti.Application.Reporting;

/// <summary>One run in the "Data used" panel of a report (P8-00 D2): the run key, what it replayed, its window, whether it was costed and the labels a report took from it.</summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="StrategyId">The strategy.</param>
/// <param name="Universe">How the universe was chosen and what it is.</param>
/// <param name="WindowStart">First India date of the run window.</param>
/// <param name="WindowEnd">Last India date of the run window.</param>
/// <param name="Costed">The run carries net R.</param>
/// <param name="CostsSheetId">The charge sheet id; null for a gross run.</param>
/// <param name="Style">The style label; null when not recorded.</param>
/// <param name="Exit">The exit label; null when not recorded.</param>
/// <param name="ClosedTrades">Closed trades the run holds (before the filters of the query).</param>
/// <param name="OpenAtEnd">Trades still open at the end of the data, left out of every report.</param>
public sealed record RunUsedView(
    string RunKey,
    string StrategyId,
    string Universe,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    bool Costed,
    string? CostsSheetId,
    string? Style,
    string? Exit,
    int ClosedTrades,
    int OpenAtEnd);
