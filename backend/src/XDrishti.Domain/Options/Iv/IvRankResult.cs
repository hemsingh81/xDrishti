namespace XDrishti.Domain.Options.Iv;

/// <summary>The rank and the percentile of a value in its window, or the closed reason there is none (both NaN then).</summary>
public readonly record struct IvRankResult(double Rank, double Percentile, string? Reason)
{
    public bool IsKnown => Reason is null;
}
