namespace XDrishti.Domain.Learning;

/// <summary>What the monitors found for one cell (P6-09).</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="ReferenceTrades">Trades known before the monitored period.</param>
/// <param name="MonitoredTrades">Trades known in it.</param>
/// <param name="Band">The band of the reference, or null when the cell could not be monitored.</param>
/// <param name="NotMonitored">Why the cell was not monitored, or null.</param>
/// <param name="Events">Alarms that fired or cleared, in order of time (then monitor).</param>
public sealed record CellDrift(string CellKey, int ReferenceTrades, int MonitoredTrades, ExpectancyBand? Band, string? NotMonitored, IReadOnlyList<DriftEvent> Events);
