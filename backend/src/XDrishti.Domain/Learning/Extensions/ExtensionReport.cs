using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The result of one feature-set comparison (P13-05): the verdict with what is missing or failed, the gain of B over A in R per candidate with its bounds, the secondary measures and the folds. Every nullable value is <c>unavailable</c> when null.
/// </summary>
/// <param name="Group">The extension that B adds to <c>tf-1</c>.</param>
/// <param name="Trial">The trial that the ledger counted.</param>
/// <param name="Options">The knobs.</param>
/// <param name="RecipeName">The name of the one recipe fitted for A and for B.</param>
/// <param name="From">First trade date of the rows.</param>
/// <param name="To">Last trade date of the rows.</param>
/// <param name="Rows">All rows given.</param>
/// <param name="Trials">N: the distinct trials of the family in the ledger.</param>
/// <param name="Factor">The multiple-testing factor for N.</param>
/// <param name="Verdict">The verdict.</param>
/// <param name="Missing">What is missing, for NOT_EVALUABLE.</param>
/// <param name="Failed">What failed the keep rule.</param>
/// <param name="Folds">Every window, used, skipped or not evaluable.</param>
/// <param name="IncompleteDropped">Windows dropped because the session list did not reach their last day.</param>
/// <param name="PairedCandidates">Candidates in the evaluable folds.</param>
/// <param name="Flips">Candidates where A and B decide differently.</param>
/// <param name="PositiveFolds">Evaluable folds with a gain above zero.</param>
/// <param name="DeltaR">Pooled gain of B over A, R per candidate.</param>
/// <param name="Stated">Bounds at the stated block.</param>
/// <param name="Sensitivity">Bounds at the sensitivity block, or null when none is run.</param>
/// <param name="DeltaLogLoss">Pooled log loss of B minus A over all used folds.</param>
/// <param name="DeltaBrier">Pooled Brier score of B minus A over all used folds.</param>
/// <param name="EceBPp">B's expected calibration error, percentage points.</param>
/// <param name="MeanR2OnFlip">The measured mean R squared over the flips.</param>
/// <param name="NaiveStandardError">The standard error that treats the rows as independent.</param>
/// <param name="DesignEffect">Bootstrap standard error over the naive one.</param>
/// <param name="Limits">The stated limits.</param>
public sealed record ExtensionReport(
    ExtensionGroup Group,
    ExtensionTrialId Trial,
    ExtensionComparisonOptions Options,
    string RecipeName,
    DateOnly From,
    DateOnly To,
    int Rows,
    int Trials,
    double Factor,
    ExtensionVerdict Verdict,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Failed,
    IReadOnlyList<ExtensionFoldResult> Folds,
    int IncompleteDropped,
    int PairedCandidates,
    int Flips,
    int PositiveFolds,
    double? DeltaR,
    BootstrapBounds Stated,
    BootstrapBounds? Sensitivity,
    double? DeltaLogLoss,
    double? DeltaBrier,
    double? EceBPp,
    double? MeanR2OnFlip,
    double? NaiveStandardError,
    double? DesignEffect,
    IReadOnlyList<string> Limits)
{
    /// <summary>Folds used (both models fitted and scored).</summary>
    public int FoldsUsed => Folds.Count(f => f.Used);

    /// <summary>Folds with both thresholds.</summary>
    public int FoldsEvaluable => Folds.Count(f => f.Evaluable);

    /// <summary>Protocol X: the extension comparison.</summary>
    public const string Protocol = "X";

    /// <summary>The row to store. <paramref name="trainingDataId"/> is the id of the training data the rows came from. The hold-out reading has its own record: <see cref="ExtensionHoldOutRecords.Build"/>.</summary>
    public ExtensionEvaluationRecord ToRecord(string trainingDataId)
    {
        ArgumentNullException.ThrowIfNull(trainingDataId);
        const string protocol = Protocol;
        const bool holdOutRead = false;
        var estimates = EstimatesJson();
        var evidence = EvidenceJson();
        var inputs = InputsJson();
        var recipeHash = RecipeHash;
        var id = Hex(string.Join('\n', "extension-evaluation/1", protocol, Trial.Hash, trainingDataId, holdOutRead ? "holdout" : "development", estimates, evidence, inputs));
        return new ExtensionEvaluationRecord(id, Trial.Family, protocol, Group.Version, VersionKeyOf(Group), TradeFeatureCatalog.Version, recipeHash, trainingDataId, Trial.Hash, Trials, Factor, Options.Unit == FoldUnit.Month ? "month" : "quarter", FoldsUsed, FoldsEvaluable, PairedCandidates, Flips, estimates, Verdict.ToWord(), Missing, Limits, evidence, inputs, holdOutRead);
    }

    internal string RecipeHash => Hex(RecipeName + ":" + Options.RecipeOptionsHash);

    /// <summary>What an evaluation is about, for the one hold-out reading per family: the version and the names, or the version and a hash of the names when that is long.</summary>
    public static string VersionKeyOf(ExtensionGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var key = group.Version + ":" + string.Join(',', group.Names);
        return key.Length <= 200 ? key : group.Version + ":sha256:" + Hex(key);
    }

    private string EstimatesJson()
    {
        using var json = new ExtensionJson();
        json.Object()
            .Number("delta_r", DeltaR).Number("lower", Stated.Lower).Number("upper", Stated.Upper)
            .Number("lower_sensitivity", Sensitivity?.Lower).Number("upper_sensitivity", Sensitivity?.Upper)
            .Number("delta_log_loss", DeltaLogLoss).Number("delta_brier", DeltaBrier).Number("ece_b_pp", EceBPp)
            .Count("positive_folds", PositiveFolds)
            .Number("flip_share", PairedCandidates > 0 ? Flips / (double)PairedCandidates : null)
            .Number("mean_r2_on_flip", MeanR2OnFlip).Number("naive_se", NaiveStandardError).Number("bootstrap_se", Stated.StandardError).Number("design_effect", DesignEffect)
            .EndObject();
        return json.Finish();
    }

    private string EvidenceJson()
    {
        using var json = new ExtensionJson();
        json.Object().Array("folds");
        foreach (var f in Folds)
        {
            json.Object().Date("start", f.Start).Date("end", f.End).Count("train_rows", f.TrainRows).Count("test_rows", f.TestRows).Flag("used", f.Used).Flag("evaluable", f.Evaluable)
                .Text("note", f.Note ?? "none").Number("threshold_a", f.ThresholdA).Number("threshold_b", f.ThresholdB).Count("candidates", f.Candidates).Count("flips", f.Flips).Number("delta_r", f.DeltaR).EndObject();
        }

        json.EndArray().Count("incomplete_windows_dropped", IncompleteDropped).Count("blocks", Stated.Blocks).Count("blocks_sensitivity", Sensitivity?.Blocks ?? 0);
        json.Array("missing");
        foreach (var m in Missing)
        {
            json.Item(m);
        }

        json.EndArray().Array("failed");
        foreach (var f in Failed)
        {
            json.Item(f);
        }

        json.EndArray().EndObject();
        return json.Finish();
    }

    private string InputsJson()
    {
        using var json = new ExtensionJson();
        json.Object().Text("trial", Trial.Text).Text("recipe", RecipeName).Date("from", From).Date("to", To).Count("rows", Rows).Unsigned("seed", Options.Seed).Count("draws", Options.Draws).EndObject();
        return json.Finish();
    }

    internal static string Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
