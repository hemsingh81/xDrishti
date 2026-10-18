namespace XDrishti.Domain.Research;

/// <summary>How the prices of a simulated trade were obtained. Stored by name, never renamed (ADR 0038 adds <c>Modelled</c>).</summary>
public enum FillBasis
{
    /// <summary>The prices of the rule table of ADR 0038: no slippage and no tick rounding of a fill (the entry limit is still an order price and is rounded).</summary>
    Exact,

    /// <summary>The fill prices carry the slippage of the run's <c>SlippageConfig</c> and lie on the price grid of the instrument's tick, always against the trade.</summary>
    Modelled,
}
