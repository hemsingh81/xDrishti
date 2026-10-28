namespace XDrishti.Domain.LiveTrading;

/// <summary>The liquidity buckets of the execution feedback (spec P11-02): L1 is the most liquid, L5 the least, <see cref="Unknown"/> when the turnover is not known. The words are stored and shown; never rename.</summary>
public static class LiquidityBucket
{
    public const string L1 = "L1";
    public const string L2 = "L2";
    public const string L3 = "L3";
    public const string L4 = "L4";
    public const string L5 = "L5";
    public const string Unknown = "unknown";

    /// <summary>All six, most liquid first, unknown last.</summary>
    public static IReadOnlyList<string> All { get; } = [L1, L2, L3, L4, L5, Unknown];
}
