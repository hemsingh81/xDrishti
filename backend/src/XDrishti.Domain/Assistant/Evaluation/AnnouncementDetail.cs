using System.Globalization;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// What set D prints beside the precision (P15-08 AC 7): the baseline's own counts, the paired comparison of per-case correctness (<c>b</c> = the baseline right and the model wrong, <c>c</c> = the model right and the baseline wrong, the exact two-sided sign test <c>p</c>),
/// the weak-evidence label, the population-weighted precision and the unstable count. Numbers only.
/// </summary>
public sealed record AnnouncementDetail(
    int Predicted, int Unstable, int BaselineTruePositive, int BaselineFalsePositive, int BaselineFalseNegative, int B, int C, double PValue, bool WeakEvidence, double? WeightedPrecision, string? HostileSet = null)
{
    /// <summary>The lines under the set in a report, in a fixed order.</summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"paired with the category baseline on correctness per case: b {B} (baseline right, model wrong), c {C} (model right, baseline wrong), exact two-sided sign test p {PValue:0.0000}"),
            WeightedPrecision is { } weighted
                ? string.Create(CultureInfo.InvariantCulture, $"population-weighted precision of material (weights recomputed from the labelled rows per stratum): {weighted:0.000}")
                : "population-weighted precision of material: n/a (no sample.json, or nothing predicted material)",
        };
        if (WeakEvidence)
        {
            lines.Add("weak evidence: the set passes, but the Wilson lower bound of the precision is under the threshold");
        }

        if (Unstable > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"unstable: {Unstable} cases changed their answer between repeats and count as not material"));
        }

        if (HostileSet is not null)
        {
            lines.Add(HostileSet);
        }

        return lines;
    }
}
