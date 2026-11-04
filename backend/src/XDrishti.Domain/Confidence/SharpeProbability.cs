namespace XDrishti.Domain.Confidence;

/// <summary>A probability from <see cref="DeflatedSharpe"/>: the value, or null with the reason it cannot be computed (<c>unavailable: &lt;reason&gt;</c>). Never a number and a reason at once.</summary>
/// <param name="Value">The probability between 0 and 1; null when unavailable.</param>
/// <param name="Unavailable">The text <c>unavailable: &lt;reason&gt;</c>; null when <paramref name="Value"/> is set.</param>
public sealed record SharpeProbability(double? Value, string? Unavailable)
{
    /// <summary>A computed probability.</summary>
    public static SharpeProbability Of(double value) => new(value, null);

    /// <summary>An unavailable probability with its reason (the prefix is added).</summary>
    public static SharpeProbability Missing(string reason) => new(null, "unavailable: " + reason);
}
