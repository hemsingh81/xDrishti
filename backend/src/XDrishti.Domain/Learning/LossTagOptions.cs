namespace XDrishti.Domain.Learning;

/// <summary>The thresholds of the loss tags and of the growing-tag detector (P6-15, ADR 0071); all are printed with the result.</summary>
/// <param name="GapMinR">A stop exit this many R beyond the stop level is a gap through the stop.</param>
/// <param name="GaveBackMfeR">A loss whose favourable excursion reached this many R gave back profit.</param>
/// <param name="HighSlippageR">Fills this many R worse than the exact prices (modelled fills only) are high slippage.</param>
/// <param name="LateAfterMinute">Minute of the day (India) from which a decision, of a trade that left on its entry date, is late (870 is 14:30).</param>
/// <param name="QuickStopMinutes">A stop hit within this many minutes of the fill is quick.</param>
/// <param name="QuickStopMaxMfeR">...if the favourable excursion stayed below this many R.</param>
/// <param name="WideStopFraction">A stop distance of at least this share of the entry price is wide (0.015 is 1.5 per cent).</param>
/// <param name="NormalVarianceMaxLossR">A stop-out whose exact R is not below minus this is the ordinary cost of the strategy.</param>
/// <param name="GrowingWindow">Losses in each of the two windows the detector compares (the latest and the one before).</param>
/// <param name="GrowingMinCount">The latest window must hold at least this many losses with the tag.</param>
/// <param name="GrowingMinRise">...and its share of losses with the tag must exceed the earlier window's by at least this much (absolute).</param>
/// <param name="ShowWorst">Worst losses listed with their numbers.</param>
public sealed record LossTagOptions(
    double GapMinR = 0.1,
    double GaveBackMfeR = 1.0,
    double HighSlippageR = 0.15,
    int LateAfterMinute = 870,
    int QuickStopMinutes = 10,
    double QuickStopMaxMfeR = 0.5,
    double WideStopFraction = 0.015,
    double NormalVarianceMaxLossR = 1.25,
    int GrowingWindow = 30,
    int GrowingMinCount = 5,
    double GrowingMinRise = 0.15,
    int ShowWorst = 10)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Range(problems, "gap_min_r", GapMinR, 0, 100);
        Range(problems, "gave_back_mfe_r", GaveBackMfeR, 0, 100);
        Range(problems, "high_slippage_r", HighSlippageR, 0, 100);
        Range(problems, "quick_stop_max_mfe_r", QuickStopMaxMfeR, 0, 100);
        Range(problems, "wide_stop_fraction", WideStopFraction, 0, 1);
        Range(problems, "normal_variance_max_loss_r", NormalVarianceMaxLossR, 1, 100);
        Range(problems, "growing_min_rise", GrowingMinRise, 0, 1);
        if (LateAfterMinute is < 0 or > 1439)
        {
            problems.Add("late_after_minute must be 0 to 1439");
        }

        if (QuickStopMinutes is < 1 or > 375)
        {
            problems.Add("quick_stop_minutes must be 1 to 375");
        }

        if (GrowingWindow is < 5 or > 100000)
        {
            problems.Add("growing_window must be 5 to 100000");
        }

        if (GrowingMinCount < 1 || GrowingMinCount > GrowingWindow)
        {
            problems.Add("growing_min_count must be 1 to growing_window");
        }

        if (ShowWorst is < 0 or > 1000)
        {
            problems.Add("show_worst must be 0 to 1000");
        }

        return problems;
    }

    private static void Range(List<string> problems, string name, double value, double low, double high)
    {
        if (!double.IsFinite(value) || value < low || value > high)
        {
            problems.Add($"{name} must be {low} to {high}");
        }
    }
}
