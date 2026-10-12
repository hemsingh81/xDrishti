namespace XDrishti.Domain.Market.Baskets;

/// <summary>What a basket is for; drives defaults later (for example long-term baskets stay out of intraday scans). Stored and sent as a code.</summary>
public enum BasketPurpose
{
    Trading,
    LongTerm,
    Watch,
}

/// <summary>The codes of <see cref="BasketPurpose"/> used in the database and the API: <c>trading</c> · <c>long_term</c> · <c>watch</c>.</summary>
public static class BasketPurposeCodes
{
    public const string Trading = "trading";
    public const string LongTerm = "long_term";
    public const string Watch = "watch";

    public static string ToCode(this BasketPurpose purpose) => purpose switch
    {
        BasketPurpose.Trading => Trading,
        BasketPurpose.LongTerm => LongTerm,
        BasketPurpose.Watch => Watch,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown basket purpose."),
    };

    /// <summary>Exact, lower-case codes only (the API contract); anything else is not a purpose.</summary>
    public static bool TryParse(string? code, out BasketPurpose purpose)
    {
        switch (code)
        {
            case Trading:
                purpose = BasketPurpose.Trading;
                return true;
            case LongTerm:
                purpose = BasketPurpose.LongTerm;
                return true;
            case Watch:
                purpose = BasketPurpose.Watch;
                return true;
            default:
                purpose = default;
                return false;
        }
    }
}
