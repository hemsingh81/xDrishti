namespace XDrishti.Domain.Market.Bars;

/// <summary>Where a gap stands (ADR 0017 §3). Stored by name — never rename a value.</summary>
public enum GapStatus
{
    /// <summary>The session has fewer 1-minute bars than it should and a refetch may still add some.</summary>
    Open,

    /// <summary>The session has all its bars now.</summary>
    Filled,

    /// <summary>A refetch added no bars (a halt, a circuit-locked day): scheduled runs leave it alone, "Re-fetch gaps" tries again.</summary>
    Unfillable,
}

/// <summary>The codes of <see cref="GapStatus"/> used in the database and the API: <c>open</c> · <c>filled</c> · <c>unfillable</c>.</summary>
public static class GapStatusCodes
{
    public const string Open = "open";
    public const string Filled = "filled";
    public const string Unfillable = "unfillable";

    public static string ToCode(this GapStatus status) => status switch
    {
        GapStatus.Open => Open,
        GapStatus.Filled => Filled,
        GapStatus.Unfillable => Unfillable,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown gap status."),
    };

    public static bool TryParse(string? code, out GapStatus status)
    {
        switch (code)
        {
            case Open:
                status = GapStatus.Open;
                return true;
            case Filled:
                status = GapStatus.Filled;
                return true;
            case Unfillable:
                status = GapStatus.Unfillable;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
