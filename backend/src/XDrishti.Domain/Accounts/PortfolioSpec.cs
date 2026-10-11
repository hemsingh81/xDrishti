namespace XDrishti.Domain.Accounts;

/// <summary>
/// The portfolio configuration of an account as the owner entered it (everything except whether the role is on).
/// <c>DefaultBucketId</c> is the custom bucket that imports go to; null means the account's own default bucket.
/// <c>Refresh</c> is the automatic holdings refresh and <c>FirstImportDate</c> the date the first import gives holdings that have none of their own
/// (ADR 0013); null leaves the current setting as it is.
/// </summary>
public sealed record PortfolioSpec(Guid? DefaultBucketId, bool IncludeInTotals, PortfolioRefresh? Refresh = null, DateOnly? FirstImportDate = null);
