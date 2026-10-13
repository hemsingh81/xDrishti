namespace XDrishti.Domain.Portfolio;

/// <summary>Which way the price must go for a price alert to trigger.</summary>
public enum AlertDirection
{
    /// <summary>Triggers when the last price is at or below the level.</summary>
    AtOrBelow,

    /// <summary>Triggers when the last price is at or above the level.</summary>
    AtOrAbove,
}

/// <summary>The stored and wire spelling of <see cref="AlertDirection"/>: <c>at_or_below</c>, <c>at_or_above</c>.</summary>
public static class AlertDirections
{
    public const string AtOrBelow = "at_or_below";

    public const string AtOrAbove = "at_or_above";

    public static string ToCode(this AlertDirection direction) => direction switch
    {
        AlertDirection.AtOrBelow => AtOrBelow,
        AlertDirection.AtOrAbove => AtOrAbove,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static bool TryParse(string? code, out AlertDirection direction)
    {
        switch (code)
        {
            case AtOrBelow:
                direction = AlertDirection.AtOrBelow;
                return true;
            case AtOrAbove:
                direction = AlertDirection.AtOrAbove;
                return true;
            default:
                direction = default;
                return false;
        }
    }
}
