using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The canonical text of an experiment's inputs and its hash (P6-11): the same inputs give the same key.</summary>
internal static class ExperimentCanonical
{
    public static string Parameters(ExperimentOptions o, int holdOutMonths) =>
        string.Create(CultureInfo.InvariantCulture, $"min_train_months={o.MinTrainMonths};test_months={o.TestMonths};min_fold_trades={o.MinFoldTrades};min_folds={o.MinFolds};folds_better_share={o.FoldsBetterShare:R};base_improvement_r={o.BaseImprovementR:R};trial_slope_r={o.TrialSlopeR:R};max_total_r_drop={o.MaxTotalRDrop:R};expected_live_gap_r={o.ExpectedLiveGapR:R};trials={o.Trials};hold_out_months={holdOutMonths}");

    // 4, 4.0 and 04 are one value, so they are one experiment.
    private static string Value(string text) => ExperimentKnob.Number(text) is { } n ? n.ToString("R", CultureInfo.InvariantCulture) : text;

    public static string Key(RunExperimentCommand c)
    {
        var text = string.Join('\n', "xd-experiment-v1", c.BaselineRunKey, c.CandidateRunKey, c.Knob.Name, c.Knob.Kind.ToString(), Value(c.Knob.BaselineValue), Value(c.Knob.CandidateValue), Parameters(c.Options, c.HoldOutMonths));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
