using System.Globalization;
using System.Numerics;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// An expected value kept as an exact fraction (spec P9-09 AC 1: 1/6, 17/13, 275/6), rounded only when an answer is graded. Reads <c>a/b</c>, an integer or a plain decimal. Pure; the denominator is always positive and the fraction is reduced.
/// </summary>
public readonly record struct EvalFraction
{
    private const int MaxDecimals = 12;

    private EvalFraction(BigInteger numerator, BigInteger denominator)
    {
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (gcd > BigInteger.One)
        {
            numerator /= gcd;
            denominator /= gcd;
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    /// <summary>Parses <c>275/6</c>, <c>-1/2</c>, <c>4</c> or <c>0.275</c>; null when it is none of these (or the denominator is zero).</summary>
    public static EvalFraction? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            return BigInteger.TryParse(text[..slash], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                && BigInteger.TryParse(text[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var d) && !d.IsZero
                ? new EvalFraction(n, d)
                : null;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Of(value);
    }

    public static EvalFraction Of(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0xFF;
        var mantissa = new BigInteger(value * Pow10Decimal(scale));
        return new EvalFraction(mantissa, BigInteger.Pow(10, scale));
    }

    public static EvalFraction Of(long value) => new(value, BigInteger.One);

    /// <summary>Half away from zero at <paramref name="decimals"/> places, as a decimal (0 to 12 places).</summary>
    public decimal Round(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaxDecimals);
        var scale = BigInteger.Pow(10, decimals);
        var twice = BigInteger.Abs(Numerator) * scale * 2;
        var scaled = (twice + Denominator) / (Denominator * 2);
        var signed = Numerator.Sign < 0 ? -scaled : scaled;
        return (decimal)signed / Pow10Decimal(decimals);
    }

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public bool EqualsDecimal(decimal value) => this == Of(value);

    public override string ToString() => Denominator.IsOne ? Numerator.ToString(CultureInfo.InvariantCulture) : Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);

    private static decimal Pow10Decimal(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
