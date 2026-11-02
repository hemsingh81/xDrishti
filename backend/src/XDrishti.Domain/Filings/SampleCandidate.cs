namespace XDrishti.Domain.Filings;

/// <summary>A stored, dated announcement the sampler may draw (P15-07): no text, only what the strata and the order need.</summary>
public sealed record SampleCandidate(Guid FilingId, string Symbol, DateTimeOffset PublishedAt, string Category);
