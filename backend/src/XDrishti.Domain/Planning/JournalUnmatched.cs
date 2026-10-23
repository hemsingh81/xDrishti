namespace XDrishti.Domain.Planning;

/// <summary>A fill the journal would not guess about (more than one ticket fits): left unmatched and stored as nothing (P7-14, ADR 0093).</summary>
/// <param name="Fill">The fill.</param>
/// <param name="Reason">Why it was not matched.</param>
public sealed record JournalUnmatched(JournalFill Fill, string Reason);
