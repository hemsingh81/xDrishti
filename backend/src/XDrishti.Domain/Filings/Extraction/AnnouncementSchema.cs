namespace XDrishti.Domain.Filings.Extraction;

/// <summary>The closed schema of the model's answer about one announcement (P15-08): the six keys, the categories, the two enums and the bounds. A change is a new schema version, which is part of every stored extraction's key.</summary>
public static class AnnouncementSchema
{
    /// <summary>The schema version stored with every extraction.</summary>
    public const string Version = "1";

    /// <summary>The longest reason, in characters.</summary>
    public const int MaxReasonLength = 200;

    /// <summary>The longest announcement text the model is shown (the loader of P15-07 already refuses longer).</summary>
    public const int MaxTextLength = 4000;

    /// <summary>The largest order value kept (rupees); a bigger number is not an order value.</summary>
    public const decimal MaxOrderValue = 1_000_000_000_000_000m;

    public const string Material = "material";
    public const string Category = "category";
    public const string Reason = "reason";
    public const string OrderValue = "order_value_inr";
    public const string Guidance = "guidance";
    public const string RatingAction = "rating_action";

    /// <summary>The six keys, exactly.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Material, Category, Reason, OrderValue, Guidance, RatingAction];

    public static IReadOnlyList<string> Categories { get; } =
    [
        "results", "order_win", "acquisition_or_merger", "fund_raising", "credit_rating", "guidance", "management_change", "litigation_or_regulatory", "default_or_delay", "dividend_or_buyback", "procedural", "other",
    ];

    public static IReadOnlyList<string> GuidanceValues { get; } = ["raised", "lowered", "maintained", "none"];

    public static IReadOnlyList<string> RatingActions { get; } = ["upgrade", "downgrade", "reaffirmed", "withdrawn", "none"];
}
