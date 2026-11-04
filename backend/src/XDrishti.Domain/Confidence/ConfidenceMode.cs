namespace XDrishti.Domain.Confidence;

/// <summary>How effective n is used (CF-01, decision 8). The programme rule: a figure is shown beside the old one and changes nothing until the owner switches it on.</summary>
public enum ConfidenceMode
{
    /// <summary>Nothing is computed or shown.</summary>
    Off = 0,

    /// <summary>Shown everywhere; no verdict and no "enough" flag changes (the default).</summary>
    ReportOnly = 1,

    /// <summary>The <c>Enough</c> flag of a cell row and gate 1 (<c>oos_trades</c>) of a verdict compare effective n with the minimum instead of n. An owner act.</summary>
    Gate = 2,
}
