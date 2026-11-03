namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Event classification grader (set D, spec P9-09 AC 7, extended by P15-08 AC 7): precision and recall of the <c>material</c> class with Wilson intervals. Below the minimum rows (200, from the thresholds) it says "not_evaluable: 200 labelled announcements arrive with P15" and
/// does not block the verdict. Precision is compared exactly (160 of 200 passes 0.80, 159 of 200 fails). Pure.
/// </summary>
public static class AnnouncementGrader
{
    /// <summary>The state of set D when the labelled file is valid but holds fewer rows than the minimum: not evaluable, and the note names the count.</summary>
    public static AnnouncementResult TooFewRows(int rows, int needed) =>
        new(SetState.NotEvaluable, rows, 0, 0, 0, null, null, null, null, $"not_evaluable: the labelled file holds {rows} announcements; {needed} are needed");

    /// <summary>The state of set D when the labelled file is present but invalid: the set fails and the note names the row.</summary>
    public static AnnouncementResult InvalidFile(string note) =>
        new(SetState.Fail, 0, 0, 0, 0, null, null, null, null, note);

    /// <summary>The state of set D when the labelled file holds enough rows but the pre-registered category baseline is missing or invalid: not evaluable, never a pass.</summary>
    public static AnnouncementResult NoBaseline(int rows) =>
        new(SetState.NotEvaluable, rows, 0, 0, 0, null, null, null, null, "not_evaluable: eval/sets/announcement-baseline.json is missing or not valid, so the model cannot be compared with the baseline");

    /// <summary>
    /// Grades the model's flags (P15-08 AC 7). The set passes only if all hold: at least the minimum rows; at least <c>min_predicted_material</c> predicted material; precision of material at least the threshold (exact: 160 of 200 passes, 159 fails); and the model beats the category
    /// baseline on correctness per case by the exact two-sided sign test (<c>c &gt; b</c> and <c>p</c> below <c>baseline_sign_p</c>). A row with no baseline class makes the comparison impossible, which is a failure, never a pass. A pass whose Wilson lower bound is under the threshold is
    /// labelled weak evidence.
    /// </summary>
    public static AnnouncementResult Grade(IReadOnlyList<AnnouncementRow> rows, ThresholdTable thresholds)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(thresholds);
        var tp = rows.Count(r => r.LabelMaterial && r.PredictedMaterial);
        var fp = rows.Count(r => !r.LabelMaterial && r.PredictedMaterial);
        var fn = rows.Count(r => r.LabelMaterial && !r.PredictedMaterial);
        if (rows.Count < thresholds.EventsMinRows)
        {
            return new AnnouncementResult(SetState.NotEvaluable, rows.Count, tp, fp, fn, null, null, null, null, $"not_evaluable: {thresholds.EventsMinRows} labelled announcements arrive with P15");
        }

        var predicted = tp + fp;
        var actual = tp + fn;
        double? precision = predicted == 0 ? null : (double)tp / predicted;
        double? recall = actual == 0 ? null : (double)tp / actual;
        (double Low, double High)? precisionInterval = predicted == 0 ? null : Wilson.Interval(tp, predicted);
        var reasons = new List<string>();
        if (predicted == 0)
        {
            reasons.Add("the model called no announcement material");
        }

        if (predicted < thresholds.EventsMinPredictedMaterial)
        {
            reasons.Add($"too_few_predicted: {predicted} predicted material, {thresholds.EventsMinPredictedMaterial} needed");
        }

        var precisionHolds = predicted > 0 && tp >= thresholds.EventsPrecisionMin * predicted;
        if (predicted > 0 && !precisionHolds)
        {
            reasons.Add("precision of material is below the threshold");
        }

        var comparable = rows.All(r => r.BaselineMaterial is not null);
        var (b, c, p) = (0, 0, 1.0);
        var (btp, bfp, bfn) = (0, 0, 0);
        if (comparable)
        {
            var paired = PairedComparison.Compare([.. rows.Select(r => r.BaselineMaterial == r.LabelMaterial)], [.. rows.Select(r => r.PredictedMaterial == r.LabelMaterial)]);
            (b, c, p) = (paired.Value.B, paired.Value.C, paired.Value.PValue);
            btp = rows.Count(r => r.LabelMaterial && r.BaselineMaterial == true);
            bfp = rows.Count(r => !r.LabelMaterial && r.BaselineMaterial == true);
            bfn = rows.Count(r => r.LabelMaterial && r.BaselineMaterial == false);
            if (!(c > b && p < (double)thresholds.EventsBaselineSignP))
            {
                reasons.Add("baseline_not_beaten: the model is not better than the category baseline by the exact sign test");
            }
        }
        else
        {
            reasons.Add("baseline_missing: a row has no baseline class, so the comparison cannot be made");
        }

        var passes = reasons.Count == 0;
        var weak = passes && precisionInterval is { } interval && interval.Low < (double)thresholds.EventsPrecisionMin;
        var weightedPredicted = rows.Where(r => r.PredictedMaterial).Sum(r => r.Weight);
        double? weighted = weightedPredicted > 0 ? rows.Where(r => r.PredictedMaterial && r.LabelMaterial).Sum(r => r.Weight) / weightedPredicted : null;
        var detail = new AnnouncementDetail(predicted, rows.Count(r => r.Unstable), btp, bfp, bfn, b, c, p, weak, weighted);
        return new AnnouncementResult(
            passes ? SetState.Pass : SetState.Fail,
            rows.Count, tp, fp, fn,
            precision, precisionInterval,
            recall, actual == 0 ? null : Wilson.Interval(tp, actual),
            passes ? null : string.Join("; ", reasons),
            detail);
    }
}
