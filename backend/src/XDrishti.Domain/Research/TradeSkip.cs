namespace XDrishti.Domain.Research;

/// <summary>Why costing left a trade out (ADR 0042, D13). The quantity is checked first, so a trade that fails both is <see cref="QuantityBelowOne"/>.</summary>
public enum TradeSkip : byte
{
    /// <summary>The sizing gives less than one share.</summary>
    QuantityBelowOne = 0,

    /// <summary>The charge in R is above the largest the context allows.</summary>
    CostAboveMax = 1,
}
