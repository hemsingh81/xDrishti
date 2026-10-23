namespace XDrishti.Domain.Learning;

/// <summary>
/// A transition the alarms would suggest for a cell (P6-09). It is a line in a report: nothing is recorded in the changelog, and the cell's real state is not read.
/// </summary>
/// <param name="CellKey">The cell.</param>
/// <param name="AtEpochSeconds">The instant of the check that raised it.</param>
/// <param name="Pause">True for "pause" (the first alarm went on), false for "recovered in shadow" (the last alarm cleared).</param>
/// <param name="Reason">The evidence of the event behind it.</param>
public sealed record DriftProposal(string CellKey, long AtEpochSeconds, bool Pause, string Reason);
