namespace XDrishti.Domain.Learning;

/// <summary>How the meta-labelling threshold is split (P6-13, doc 16 <c>split_by</c>): one threshold, or one per group of the cell key.</summary>
public enum MetaSplit
{
    /// <summary>One threshold for all candidates.</summary>
    None,

    /// <summary>One per style (intraday, swing).</summary>
    Style,

    /// <summary>One per side (BUY, SELL).</summary>
    Side,

    /// <summary>One per style and side.</summary>
    StyleSide,
}
