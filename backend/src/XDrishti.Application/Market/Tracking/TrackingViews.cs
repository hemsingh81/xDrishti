namespace XDrishti.Application.Market.Tracking;

/// <summary>Instruments to track or untrack: 1 to 500 ids (duplicates are fine and count once).</summary>
public sealed record TrackingInput(IReadOnlyList<Guid>? InstrumentIds);

/// <summary>
/// What a track call did (all counts are of distinct instruments): <c>added</c> never tracked before, <c>reactivated</c> tracked again after an
/// untrack, <c>alreadyTracked</c> nothing to do. <c>totalTracked</c> is the number tracked now.
/// </summary>
public sealed record TrackResult(int Requested, int Added, int Reactivated, int AlreadyTracked, int TotalTracked);

/// <summary>What an untrack call did: <c>untracked</c> were tracked, <c>notTracked</c> were not (nothing to do). <c>totalTracked</c> is the number tracked now.</summary>
public sealed record UntrackResult(int Requested, int Untracked, int NotTracked, int TotalTracked);

/// <summary>Pasted tickers, canonical symbols (<c>NSE:RELIANCE</c>) or ISINs, up to 5,000 entries.</summary>
public sealed record ResolveInput(IReadOnlyList<string>? Symbols);

/// <summary>An entry that named an instrument. <c>term</c> is the entry as typed (the first one when several name the same instrument); <c>tracked</c> = tracked now; <c>retired</c> = cannot be newly tracked.</summary>
public sealed record ResolvedInstrument(string Term, Guid Id, string Symbol, string Name, string Type, string? Isin, bool Retired, bool Tracked);

/// <summary>An entry that named no instrument. <c>reason</c>: <c>not_found</c> · <c>ambiguous</c> (an ISIN of several instruments) · <c>invalid</c> (too long or with control characters).</summary>
public sealed record UnmatchedEntry(string Term, string Reason);

/// <summary>
/// The outcome of looking up a pasted list. <c>entries</c> counts the non-blank entries, <c>duplicates</c> those collapsed because an earlier entry
/// was the same text or named the same instrument. Entries that match nothing are never guessed: they are listed in <c>unmatched</c>.
/// </summary>
public sealed record ResolveResult(IReadOnlyList<ResolvedInstrument> Matched, IReadOnlyList<UnmatchedEntry> Unmatched, int Entries, int Duplicates);
