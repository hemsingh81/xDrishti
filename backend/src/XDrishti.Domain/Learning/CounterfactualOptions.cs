namespace XDrishti.Domain.Learning;

/// <summary>The knobs of the frozen-model counterfactual (P6-12, ADR 0077); all are printed with the result.</summary>
/// <param name="FrozenMonths">How long the frozen model is left alone before the end of the data, at most 12 (the data hold about two years; the period is shortened further when the history before the cut would be under the minimum train months).</param>
/// <param name="RetrainMonths">The retrained side is re-fitted at the start of each window of this many months (expanding).</param>
/// <param name="MinWindowRows">A window with fewer rows is not judged.</param>
/// <param name="MinJudgedWindows">Fewer judged windows than this give inconclusive. Judged windows are those in which the retrained model has seen labels the frozen one has not, so the first window never counts.</param>
/// <param name="MinGainLogLoss">The log loss gain of retraining (frozen minus retrained, per row) must exceed this, in the windows and pooled (times the multiple-testing factor); the same in the other direction for "frozen is better".</param>
/// <param name="SignificanceZ">The pooled paired gain must exceed this many standard errors (times the multiple-testing factor).</param>
/// <param name="Trials">How many settings have been looked at; the margins widen with <c>MultipleTesting.Factor</c>.</param>
public sealed record CounterfactualOptions(
    int FrozenMonths = 12,
    int RetrainMonths = 3,
    int MinWindowRows = 10,
    int MinJudgedWindows = 3,
    double MinGainLogLoss = 0.005,
    double SignificanceZ = 1.645,
    int Trials = 1)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (FrozenMonths is < 1 or > 12)
        {
            problems.Add("frozen_months must be 1 to 12 (the data allow about one year of stopped learning)");
        }

        if (RetrainMonths is < 1 or > 12)
        {
            problems.Add("retrain_months must be 1 to 12");
        }

        if (MinWindowRows is < 1 or > 100000)
        {
            problems.Add("min_window_rows must be 1 to 100000");
        }

        if (MinJudgedWindows is < 1 or > 1000)
        {
            problems.Add("min_judged_windows must be 1 to 1000");
        }

        if (!double.IsFinite(MinGainLogLoss) || MinGainLogLoss is < 0 or > 10)
        {
            problems.Add("min_gain_log_loss must be 0 to 10");
        }

        if (!double.IsFinite(SignificanceZ) || SignificanceZ is < 0 or > 10)
        {
            problems.Add("significance_z must be 0 to 10");
        }

        if (Trials < 1)
        {
            problems.Add("trials must be at least 1");
        }

        return problems;
    }
}
