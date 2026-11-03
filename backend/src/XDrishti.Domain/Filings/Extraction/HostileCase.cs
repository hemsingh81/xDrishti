namespace XDrishti.Domain.Filings.Extraction;

/// <summary>One announcement written to attack the extractor (P15-08 AC 3): a fake company, a subject and a text. Not data from any filing.</summary>
public sealed record HostileCase(string Id, string Subject, string Text);
