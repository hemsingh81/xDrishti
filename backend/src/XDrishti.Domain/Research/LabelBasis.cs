namespace XDrishti.Domain.Research;

/// <summary>Which result of a trade the label reports as its R at resolution (P6-03, doc 09 §9). The label itself never depends on it. Stored by name, never renamed.</summary>
public enum LabelBasis
{
    /// <summary><see cref="SimTrade.RExact"/>: exact prices, no slippage, no charges.</summary>
    Exact,

    /// <summary><see cref="SimTrade.RFilled"/>: filled prices, slippage included, no charges.</summary>
    Filled,

    /// <summary><see cref="TradeCosting.RNet"/>: filled prices and the charge sheet.</summary>
    Net,
}
