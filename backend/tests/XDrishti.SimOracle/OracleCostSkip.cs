namespace XDrishti.SimOracle;

/// <summary>Why a trade was not kept by costing (ADR 0042 D13: quantity first, then cost).</summary>
public enum OracleCostSkip
{
    None,
    Quantity,
    Cost,
}
