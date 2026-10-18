namespace XDrishti.Strategies.Model;

/// <summary>The closed set of stop kinds of schema 1 (doc 11 §2, <i>Stops</i>). <c>level</c> is not among them: a computed price cannot be written in the language (ADR 0036).</summary>
public enum StopKind
{
    /// <summary>A fixed percentage of the entry price (<c>pct</c>).</summary>
    FixedPct,

    /// <summary>A multiple of the ATR (<c>mult</c>, optional <c>atr_period</c>).</summary>
    AtrMult,

    /// <summary>Below the lowest low of the last <c>lookback</c> closed bars (BUY).</summary>
    SwingLow,

    /// <summary>Above the highest high of the last <c>lookback</c> closed bars (SELL).</summary>
    SwingHigh,

    /// <summary>Below the low of the trigger bar (BUY).</summary>
    BarLow,

    /// <summary>Above the high of the trigger bar (SELL).</summary>
    BarHigh,

    /// <summary>Beyond the opposite end of the opening range of <c>minutes</c> minutes.</summary>
    OrOpposite,
}
