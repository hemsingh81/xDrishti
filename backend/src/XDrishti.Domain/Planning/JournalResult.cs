namespace XDrishti.Domain.Planning;

/// <summary>What the matcher made of a day's fills (P7-14).</summary>
/// <param name="Links">Entry, exit and discretionary links, in a fixed order.</param>
/// <param name="Unmatched">Fills left alone because the match was ambiguous.</param>
public sealed record JournalResult(IReadOnlyList<JournalLink> Links, IReadOnlyList<JournalUnmatched> Unmatched);
