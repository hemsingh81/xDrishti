namespace XDrishti.Domain.Filings;

/// <summary>A stored results envelope with the facts parsed from it by one parser version (none when it was not parsed, rejected or held no facts).</summary>
public sealed record ResultFilingFacts(StoredFiling Filing, IReadOnlyList<StoredResultFact> Facts);
