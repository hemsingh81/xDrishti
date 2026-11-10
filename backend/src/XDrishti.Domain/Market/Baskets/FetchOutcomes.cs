namespace XDrishti.Domain.Market.Baskets;

/// <summary>Outcomes of one fetch (<c>basket_fetches.outcome</c>); part of the API contract.</summary>
public static class FetchOutcomes
{
    public const string Unchanged = "unchanged";
    public const string Applied = "applied";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}
