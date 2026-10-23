namespace XDrishti.Domain.Learning;

/// <summary>The closed trades of one cell, for the drift replay (P6-09).</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Trades">Every closed trade of the cell, reference and monitored together; the replay splits them by time.</param>
public sealed record DriftCellInput(string CellKey, IReadOnlyList<DriftObservation> Trades);
