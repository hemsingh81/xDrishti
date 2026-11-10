namespace XDrishti.Domain.Market.Baskets;

/// <summary>Outcomes of one derived-basket version (<c>basket_versions.outcome</c>, BK-03); part of the API contract. A failure lives on the service run, never on a version.</summary>
public static class VersionOutcomes
{
    public const string Applied = "applied";
    public const string Unchanged = "unchanged";
    public const string NotFormed = "not_formed";
}
