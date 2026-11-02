namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The record of the one hold-out reading (P13-05). It carries the hold-out outcome, not the development verdict: a confirmed reading is KEEP, a reading that did not confirm is INCONCLUSIVE, an unreadable one is NOT_EVALUABLE with the reason named.</summary>
public static class ExtensionHoldOutRecords
{
    /// <summary>The row to store for <paramref name="result"/>, the hold-out reading of the KEEP in <paramref name="report"/>.</summary>
    public static ExtensionEvaluationRecord Build(ExtensionReport report, ExtensionHoldOutResult result, string trainingDataId)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(trainingDataId);
        var verdict = result.Status switch
        {
            ExtensionHoldOutStatus.Confirmed => ExtensionVerdict.Keep,
            ExtensionHoldOutStatus.NotConfirmed => ExtensionVerdict.Inconclusive,
            _ => ExtensionVerdict.NotEvaluable,
        };
        string[] missing = result.Status == ExtensionHoldOutStatus.NotEvaluable ? [result.Note ?? "hold-out not evaluable"] : [];
        using var estimates = new ExtensionJson();
        estimates.Object().Text("status", result.Status.ToString()).Count("hold_out_rows", result.Rows).Number("delta_r", result.DeltaR).Number("log_loss_a", result.LogLossA).Number("log_loss_b", result.LogLossB)
            .Number("threshold_a", result.ThresholdA).Number("threshold_b", result.ThresholdB).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("note", result.Note ?? "none").Text("development_verdict", report.Verdict.ToWord()).Number("development_delta_r", report.DeltaR).EndObject();
        using var inputs = new ExtensionJson();
        inputs.Object().Text("trial", report.Trial.Text).Text("recipe", report.RecipeName).Date("from", report.From).Date("to", report.To).EndObject();
        var e = estimates.Finish();
        var v = evidence.Finish();
        var i = inputs.Finish();
        var id = ExtensionReport.Hex(string.Join('\n', "extension-evaluation/1", ExtensionReport.Protocol, report.Trial.Hash, trainingDataId, "holdout", e, v, i));
        return new ExtensionEvaluationRecord(
            id, report.Trial.Family, ExtensionReport.Protocol, report.Group.Version, ExtensionReport.VersionKeyOf(report.Group), TradeFeatureCatalog.Version, report.RecipeHash, trainingDataId, report.Trial.Hash, report.Trials, report.Factor,
            report.Options.Unit == FoldUnit.Month ? "month" : "quarter", report.FoldsUsed, report.FoldsEvaluable, result.Rows, null, e, verdict.ToWord(), missing,
            ["The hold-out is read once; this record is that reading.", "Advisory research only: nothing in a decision path reads this result."], v, i, true);
    }
}
