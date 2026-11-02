namespace XDrishti.Domain.Filings;

/// <summary>One (half, category, year) stratum of the sample with its eligible and sampled counts.</summary>
public sealed record SampleStratum(string Half, string Category, int Year, int Eligible, int Sampled);
