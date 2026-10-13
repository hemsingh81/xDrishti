namespace XDrishti.Application.Portfolio.Groups;

/// <summary>What holdings are grouped by. Wire names: <c>account</c>, <c>bucket</c>, <c>sector</c>, <c>basket</c>.</summary>
internal enum GroupDimension
{
    Account,
    Bucket,
    Sector,
    Basket,
}

internal static class GroupDimensions
{
    public static bool TryParse(string? text, out GroupDimension dimension)
    {
        var parsed = text?.Trim().ToLowerInvariant() switch
        {
            "account" => GroupDimension.Account,
            "bucket" => GroupDimension.Bucket,
            "sector" => GroupDimension.Sector,
            "basket" => GroupDimension.Basket,
            _ => (GroupDimension?)null,
        };
        dimension = parsed.GetValueOrDefault();
        return parsed is not null;
    }

    public static string Name(GroupDimension dimension) => dimension.ToString().ToLowerInvariant();

    /// <summary>Accounts, buckets and sectors partition the holdings; baskets overlap.</summary>
    public static bool IsAdditive(GroupDimension dimension) => dimension != GroupDimension.Basket;

    /// <summary>The second dimension of the stacked bar: accounts for sectors, sectors for accounts; none for the others.</summary>
    public static GroupDimension? StackFor(GroupDimension dimension) => dimension switch
    {
        GroupDimension.Sector => GroupDimension.Account,
        GroupDimension.Account => GroupDimension.Sector,
        _ => null,
    };
}
