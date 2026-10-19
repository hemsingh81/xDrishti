namespace XDrishti.Domain.Research;

/// <summary>Whether the result of a simulated trade is net of charges. Stored by name, never renamed.</summary>
public enum CostBasis
{
    /// <summary>No brokerage, taxes or charges are taken off (ADR 0037, decision D7).</summary>
    None,

    /// <summary>The trade was sized and its two orders charged by a charge sheet; <see cref="SimTrade.Costing"/> says how (ADR 0042).</summary>
    Charged,
}
