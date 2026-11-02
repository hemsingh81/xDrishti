namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Names a listed file and the hash it had when listed, so that opening it can prove it did not change.</summary>
public sealed record FilingRef(string FileName, string Sha256);
