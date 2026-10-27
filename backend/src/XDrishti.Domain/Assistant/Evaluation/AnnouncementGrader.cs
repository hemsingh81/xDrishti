namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Event classification grader (set D, spec P9-09 AC 7): precision and recall of the <c>material</c> class with Wilson intervals. Below the minimum rows (200, from the thresholds) it says "not_evaluable: 200 labelled announcements arrive with P15" and
/// does not block the verdict. Precision is compared exactly (160 of 200 passes 0.80, 159 of 200 fails). Pure.
/// </summary>
public static class AnnouncementGrader
{
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
        var passes = predicted > 0 && tp >= thresholds.EventsPrecisionMin * predicted;
        return new AnnouncementResult(
            passes ? SetState.Pass : SetState.Fail,
            rows.Count, tp, fp, fn,
            precision, predicted == 0 ? null : Wilson.Interval(tp, predicted),
            recall, actual == 0 ? null : Wilson.Interval(tp, actual),
            passes ? null : predicted == 0 ? "the model called no announcement material" : "precision of material is below the threshold");
    }
}
