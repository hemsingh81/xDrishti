namespace XDrishti.Domain.Paper;

/// <summary>The backtest trades of one cell known before the epoch started (spec P10-04), unweighted: what the band resamples.</summary>
/// <param name="CellKey">The cell key.</param>
/// <param name="Results">R of each closed backtest trade of the cell with an exit date before the epoch's start.</param>
public sealed record CellReference(string CellKey, IReadOnlyList<double> Results);
