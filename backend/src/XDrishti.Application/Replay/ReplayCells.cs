namespace XDrishti.Application.Replay;

/// <summary>The cells of a session.</summary>
/// <param name="Cells">The cash cells.</param>
/// <param name="Notes">Plain notes, for example the label that the lifecycle is as recorded.</param>
public sealed record ReplayCells(IReadOnlyList<ReplayCell> Cells, IReadOnlyList<string> Notes);
