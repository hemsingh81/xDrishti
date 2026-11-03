namespace XDrishti.Domain.PairResearch;

/// <summary>One share of the universe in a formation: its canonical symbol, its sector (today's label), its daily closes over the formation window (one per session, the same sessions for every member) and the mean turnover of the last 20 sessions at the window end.</summary>
/// <param name="Symbol">The canonical symbol.</param>
/// <param name="Sector">Today's sector, or null.</param>
/// <param name="Closes">The last 1-minute close of each session of the window, ascending.</param>
/// <param name="MeanTurnover">Mean of close times volume over the last 20 sessions of the window.</param>
public sealed record PairMember(string Symbol, string? Sector, IReadOnlyList<double> Closes, double MeanTurnover);
