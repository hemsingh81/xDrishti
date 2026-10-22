namespace XDrishti.Domain.Research;

/// <summary>
/// What the replay of one instrument gives for a comparison (P4-05): the replay of each candidate in the order of the candidates, and for each the number of entries (decided in the window) that the position gate of a plain run would have
/// skipped under that exit (D4; 0 for an intraday strategy, which is not gated). A plain strategy has one member.
/// </summary>
/// <param name="Members">One <see cref="InstrumentReplay"/> per candidate.</param>
/// <param name="GatedBlocked">The gated count of each candidate.</param>
public sealed record ComparisonReplay(IReadOnlyList<InstrumentReplay> Members, IReadOnlyList<long> GatedBlocked);
