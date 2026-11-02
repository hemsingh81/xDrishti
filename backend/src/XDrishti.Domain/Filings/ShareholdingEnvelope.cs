namespace XDrishti.Domain.Filings;

/// <summary>One stored shareholding group: its envelope (the group's time is <c>BroadcastTs</c>) and its category lines.</summary>
public sealed record ShareholdingEnvelope(StoredFiling Filing, IReadOnlyList<ShareholdingLine> Lines);
