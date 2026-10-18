namespace XDrishti.Domain.Research;

/// <summary>Whether the result of a simulated trade is net of charges. Stored by name, never renamed. P3-03c adds <c>Charged</c>.</summary>
public enum CostBasis
{
    /// <summary>No brokerage, taxes or charges are taken off (ADR 0037, decision D7).</summary>
    None,
}
