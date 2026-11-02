namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extension <c>fu-1</c> (P15-05, ADR 0185): the fundamental features, in the order they are stored.</summary>
public static class FundamentalFeatureCatalog
{
    public const string Extension = "fu";

    public const string Version = "fu-1";

    public static readonly IReadOnlyList<string> Names = ["sue_yoy", "sue_ttm", "rev_growth_yoy", "margin_chg_yoy", "promoter_pledge_pct", "promoter_chg_qoq", "pe_pctile_5y"];

    public static ExtensionGroup Group { get; } = new(Extension, Version, Names);
}
