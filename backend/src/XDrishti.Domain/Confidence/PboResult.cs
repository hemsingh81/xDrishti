namespace XDrishti.Domain.Confidence;

/// <summary>
/// The probability of backtest overfitting from <see cref="Cscv"/>: the figures, or none with the reason (<c>unavailable: &lt;reason&gt;</c>). Never a number and a reason at once.
/// </summary>
/// <param name="Pbo">The share of splits whose logit is at or below 0, between 0 and 1; null when unavailable.</param>
/// <param name="MedianLogit">The median logit over the splits; null when unavailable.</param>
/// <param name="Variants">The variants compared.</param>
/// <param name="Blocks">S, the blocks the window was cut into.</param>
/// <param name="Splits">The splits tried, <c>C(S, S/2)</c>; 0 when unavailable.</param>
/// <param name="Unavailable">The text <c>unavailable: &lt;reason&gt;</c>; null when <paramref name="Pbo"/> is set.</param>
public sealed record PboResult(double? Pbo, double? MedianLogit, int Variants, int Blocks, int Splits, string? Unavailable)
{
    /// <summary>An unavailable result with its reason (the prefix is added).</summary>
    public static PboResult Missing(string reason, int variants, int blocks) => new(null, null, variants, blocks, 0, "unavailable: " + reason);
}
