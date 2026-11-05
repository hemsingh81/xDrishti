using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// One comparison of two arms of the loop test (spec CF-12, D4): the difference is <c>first arm minus second arm</c> per candidate (R if the arm takes the candidate, 0 if it skips it), summed by trade day and resampled by day.
/// </summary>
/// <param name="Name">A stable code: <c>retrained_vs_frozen</c> or <c>retrained_vs_no_ml</c>.</param>
/// <param name="Verdict">The reading.</param>
/// <param name="Candidates">Candidates compared.</param>
/// <param name="Days">Distinct trade days among them.</param>
/// <param name="MeanDifference">Mean difference per candidate on the candidates as they are; null when not available.</param>
/// <param name="Low">Lower end of the two-sided interval of the mean difference; null when not available.</param>
/// <param name="High">Upper end; null when not available.</param>
/// <param name="ProbabilityBetter">The share of resamples in which the first arm is better (a tie is not better); null when not available.</param>
/// <param name="ProbabilityWorse">The share in which the first arm is worse; null when not available.</param>
/// <param name="Text">The words, with the numbers.</param>
public sealed record ArmComparison(string Name, LoopVerdict Verdict, int Candidates, int Days, double? MeanDifference, double? Low, double? High, double? ProbabilityBetter, double? ProbabilityWorse, string Text)
{
    /// <summary>A comparison that could not be made, with the reason.</summary>
    public static ArmComparison Unavailable(string name, string reason, int candidates = 0) =>
        new(name, LoopVerdict.Unavailable, candidates, 0, null, null, null, null, null, "unavailable: " + reason);

    /// <summary>The words of a comparison that was made.</summary>
    internal static string Describe(string name, LoopVerdict verdict, double mean, double low, double high, double better, double worse, int days, double level) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}: {verdict} - mean difference {mean:F4} per candidate, {level * 100:F0}% interval [{low:F4}, {high:F4}], P(first arm better) {better:F4}, P(worse) {worse:F4}, {days} days");
}
