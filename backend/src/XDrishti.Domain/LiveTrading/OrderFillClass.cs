namespace XDrishti.Domain.LiveTrading;

/// <summary>The class of one entry order for the capacity score (spec P11-02 definitions). <c>full</c>, <c>partial</c>, <c>rejected</c> and <c>queue_miss</c> are counted; <c>price_miss</c> and <c>unclassified</c> are reported apart and never counted. Stored words; never rename.</summary>
public static class OrderFillClass
{
    public const string Full = "full";
    public const string Partial = "partial";
    public const string Rejected = "rejected";
    public const string QueueMiss = "queue_miss";
    public const string PriceMiss = "price_miss";
    public const string Unclassified = "unclassified";

    public static IReadOnlyList<string> All { get; } = [Full, Partial, Rejected, QueueMiss, PriceMiss, Unclassified];

    /// <summary>Whether the class counts toward the capacity denominator.</summary>
    public static bool Counted(string cls) => cls is Full or Partial or Rejected or QueueMiss;
}
