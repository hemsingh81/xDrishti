namespace XDrishti.Strategies.Mirror;

/// <summary>The problem codes of the BUY to SELL mirror (spec P3-02e). Every one is an error except <see cref="Applied"/>.</summary>
public static class MirrorCodes
{
    private const string Prefix = "strategy.mirror.";

    /// <summary>The SELL side was produced by the mirror; the message holds the printed block. Information.</summary>
    public const string Applied = Prefix + "applied";

    /// <summary>The condition reads the market context or the calendar (regime, VIX, breadth, index, day of week, expiry day); whether the SELL wants the same filter or the opposite one is the author's call.</summary>
    public const string ContextName = Prefix + "context_name";

    /// <summary>The shape has no single mirror (a product or ratio of prices, a sum of prices, a bare price in <c>abs</c>).</summary>
    public const string NotMirrorable = Prefix + "not_mirrorable";

    /// <summary>A price is compared with a bare number (<c>close &gt; 100</c>): the mirror of an absolute level is not defined.</summary>
    public const string AbsoluteLevel = Prefix + "absolute_level";

    /// <summary>The two sides of a comparison are of different kinds (a price against an oscillator).</summary>
    public const string MixedOrientation = Prefix + "mixed_orientation";

    /// <summary>A literal has more than 15 significant digits, so its complement cannot be written exactly.</summary>
    public const string LiteralPrecision = Prefix + "literal_precision";

    /// <summary>An atom the table has no row for. Cannot happen for the catalog while the completeness test passes; the condition does not compile either, and its own problems are the ones reported.</summary>
    public const string UnknownName = Prefix + "unknown_name";
}
