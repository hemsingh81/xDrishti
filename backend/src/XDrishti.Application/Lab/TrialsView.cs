using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The count of looks at a strategy family with its parts (spec CF-06, D3, AC12), as of the judgement. <c>HonestFactor</c> is the factor of the honest total; <c>FactorUsed</c> is the one the gates were tightened by, which is the old distinct-hash factor
/// until the owner turns <c>Confidence:Registration:CountInFactor</c> on (<c>HonestCountApplied</c>).
/// </summary>
public sealed record TrialsView(
    int RunHashes,
    int SweepPoints,
    int ExtraExits,
    int Replans,
    int NullRows,
    int Total,
    double HonestFactor,
    int JudgedCount,
    double FactorUsed,
    bool HonestCountApplied,
    string Text)
{
    /// <summary>The view of the parts; <paramref name="adjustmentOn"/> is the Lab's own multiple-testing switch (off: one look, factor 1).</summary>
    public static TrialsView Of(TrialCounts parts, bool honestApplied, bool adjustmentOn)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var total = parts.Total;
        var honest = MultipleTesting.Factor(MultipleTesting.Effective(total, adjustmentOn));
        var judged = MultipleTesting.Effective(TrialCounter.Judged(parts, honestApplied), adjustmentOn);
        var used = MultipleTesting.Factor(judged);
        var c = CultureInfo.InvariantCulture;
        var text = $"{total.ToString(c)} looks = {parts.RunHashes.ToString(c)} strategy hashes + {parts.SweepPoints.ToString(c)} sweep points + {parts.ExtraExits.ToString(c)} extra exit candidates + {parts.Replans.ToString(c)} re-plans + {parts.NullRows.ToString(c)} nulls; "
            + $"honest factor {honest.ToString("0.0000", c)}; the gates were tightened by {used.ToString("0.0000", c)} ({(honestApplied ? "the honest count" : $"{parts.DistinctHashes.ToString(c)} distinct hashes, as before; the honest count is shown beside it")}).";
        if (parts.TrainingSweeps > 0)
        {
            // shown beside the count, never inside it: no link from a tuned strategy to the sweep it came from exists yet, so the deflation cannot be applied honestly (ADR 0243)
            text += $" {parts.TrainingSweeps.ToString(c)} training sweeps ({parts.TrainingSweepTrials.ToString(c)} variants) are recorded separately and are not in this count, because nothing links a strategy tuned from a sweep to it yet.";
        }

        return new TrialsView(parts.RunHashes, parts.SweepPoints, parts.ExtraExits, parts.Replans, parts.NullRows, total, honest, judged, used, honestApplied, text);
    }
}
