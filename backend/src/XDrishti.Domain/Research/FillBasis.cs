namespace XDrishti.Domain.Research;

/// <summary>How the prices of a simulated trade were obtained. Stored by name, never renamed. P3-03b adds <c>Modelled</c>.</summary>
public enum FillBasis
{
    /// <summary>The model's price with no slippage, no tick rounding and no entry limit (ADR 0037, decision D7).</summary>
    Exact,
}
