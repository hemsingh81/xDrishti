namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extensions the system extracts itself (P15-05): <c>ev-1</c> and <c>fu-1</c>.</summary>
public static class ExtensionCatalogs
{
    /// <summary>The group of an extension version, or null for any other.</summary>
    public static ExtensionGroup? GroupOf(string? version) => version switch
    {
        EventFeatureCatalog.Version => EventFeatureCatalog.Group,
        FundamentalFeatureCatalog.Version => FundamentalFeatureCatalog.Group,
        _ => null,
    };
}
