namespace XDrishti.Domain.LiveTrading;

/// <summary>The non-inferiority verdict of the live execution against paper (spec P11-03 criterion 3): <c>within</c> when the one-sided upper bound of the mean shortfall is at most the margin, <c>not_within</c> when the one-sided lower bound is above it, otherwise <c>inconclusive</c>; <c>insufficient</c> under the minimum pairs.</summary>
public static class NonInferiority
{
    public const string Within = "within";
    public const string NotWithin = "not_within";
    public const string Inconclusive = "inconclusive";
    public const string Insufficient = "insufficient";

    public static string Verdict(int n, double upperOneSided, double lowerOneSided, double margin, int minPairs) =>
        n < minPairs ? Insufficient : upperOneSided <= margin ? Within : lowerOneSided > margin ? NotWithin : Inconclusive;
}
