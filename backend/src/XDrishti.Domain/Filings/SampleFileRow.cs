namespace XDrishti.Domain.Filings;

/// <summary>One row of <c>sample.json</c>: no text, only the id, the filing, the half, the role, the stratum counts, the inclusion weight and the hash of the text columns.</summary>
public sealed record SampleFileRow(string Id, Guid FilingId, string Half, bool Reserve, int StratumEligible, int StratumSampled, double Weight, string RowSha256);
