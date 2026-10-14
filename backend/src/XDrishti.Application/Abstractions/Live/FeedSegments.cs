namespace XDrishti.Application.Abstractions.Live;

/// <summary>Dhan's names of the exchange segments the feed carries (the same words the master list stores as the segment of a broker id).</summary>
public static class FeedSegments
{
    /// <summary>NSE equity: shares and ETFs.</summary>
    public const string NseEquity = "NSE_EQ";

    /// <summary>Indices.</summary>
    public const string Index = "IDX_I";
}
