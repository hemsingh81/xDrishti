namespace XDrishti.Domain.Filings;

/// <summary>One row of the committed manifest: ids and hashes only, never text.</summary>
public sealed record ManifestRow(string Id, Guid FilingId, string RowSha256);
