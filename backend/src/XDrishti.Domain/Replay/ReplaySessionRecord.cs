namespace XDrishti.Domain.Replay;

/// <summary>
/// One replayed session S (SR-01): the plan made at the close of S, and what the replay of it gave. <paramref name="Fills"/> are the entries of the taken items on the next session; <paramref name="Exits"/> are the trades that closed on S itself.
/// <paramref name="Equity"/> and <paramref name="DrawdownPct"/> are the ledger's at the close of S and use nothing later.
/// </summary>
/// <param name="Session">S.</param>
/// <param name="Candidates">Every candidate found.</param>
/// <param name="Ranked">The ranked list.</param>
/// <param name="Taken">The items taken.</param>
/// <param name="Blocked">The items not taken, one reason each.</param>
/// <param name="Fills">Entries on the next session.</param>
/// <param name="Exits">Symbols of the trades that exited on S.</param>
/// <param name="Equity">Capital plus realised result at the close of S.</param>
/// <param name="DrawdownPct">Largest fall of the realised result so far, percent; null without a basis.</param>
/// <param name="OpenRiskPct">Open risk at the close of S, percent of equity.</param>
/// <param name="OpenPositions">Positions open at the close of S.</param>
/// <param name="Labels">The hindsight labels of the session.</param>
/// <param name="Notes">Plain notes (end of range, data missing).</param>
public sealed record ReplaySessionRecord(
    DateOnly Session,
    IReadOnlyList<ReplayCandidateRow> Candidates,
    IReadOnlyList<ReplayCandidateRow> Ranked,
    IReadOnlyList<ReplayCandidateRow> Taken,
    IReadOnlyList<ReplayBlocked> Blocked,
    IReadOnlyList<ReplayFill> Fills,
    IReadOnlyList<string> Exits,
    decimal Equity,
    decimal? DrawdownPct,
    decimal? OpenRiskPct,
    int OpenPositions,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Notes);
