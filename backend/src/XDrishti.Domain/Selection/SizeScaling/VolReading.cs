namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>The volatility of a share as of the previous close: a number and its scale, or why there is none (never a guess).</summary>
/// <param name="Status"><c>ok</c>, <c>insufficient_returns</c> (fewer than the minimum returns) or <c>insufficient_history</c> (no target).</param>
/// <param name="Returns">How many usable returns there were.</param>
/// <param name="Sigma">Today's EWMA volatility (a daily fraction); null unless there were enough returns.</param>
/// <param name="Target">The median of the share's own EWMA volatility over the previous 252 sessions; null unless there was enough history.</param>
/// <param name="Scale">The volatility scale in the floor to 1.0; null unless <paramref name="Status"/> is <c>ok</c>.</param>
public sealed record VolReading(string Status, int Returns, double? Sigma, double? Target, double? Scale)
{
    /// <summary>The status of a usable reading.</summary>
    public const string Ok = "ok";

    /// <summary>Volatility over its usual level (sigma over target); null when there is none.</summary>
    public double? Ratio => Sigma is { } s && Target is { } t && t > 0 ? s / t : null;
}
