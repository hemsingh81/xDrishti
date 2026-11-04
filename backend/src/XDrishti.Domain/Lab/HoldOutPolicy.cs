using XDrishti.Domain.Confidence;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Lab;

/// <summary>
/// How long the locked hold-out is and where it starts (spec CF-06, D4): <c>max(minimum months, share of the stored history)</c>, rounded up to whole months, ending at the last stored session. Pure arithmetic over dates and options; it stores nothing (the start is fixed once by the application).
/// </summary>
public static class HoldOutPolicy
{
    /// <summary>The average month in days, the unit of "months of history".</summary>
    public const double DaysPerMonth = 365.25 / 12;

    /// <summary>The sizing from months of history. 25.2 months with the defaults gives 6 (20 % is 5.04), development 19.2 and 3 folds; 60 months gives 12, 48 and 13; 12 months gives 6, 6 and <c>no_folds</c>.</summary>
    public static HoldOutPlan FromMonths(double historyMonths, HoldOutOptions options, SelectionOptions selection)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selection);
        var safe = double.IsFinite(historyMonths) ? Math.Max(0, historyMonths) : 0;
        var share = Math.Round(safe * options.SharePercent / 100, 6);
        var length = (int)Math.Ceiling(Math.Max(options.MinMonths, share));
        var development = Math.Max(0, safe - length);
        if (development <= 0)
        {
            return new HoldOutPlan(safe, length, 0, 0, null, "no_history");
        }

        var folds = development + 1e-9 < selection.MinTrainMonths ? 0 : (int)Math.Floor(((development - selection.MinTrainMonths) / selection.TestMonths) + 1e-9);
        return new HoldOutPlan(safe, length, Math.Round(development, 6), folds, null, folds == 0 ? "no_folds" : "ok");
    }

    /// <summary>The sizing from the first and last stored session; the start is <c>last - length months + 1 day</c>, the cut of <c>HoldOut.Reserve</c>.</summary>
    public static HoldOutPlan FromDates(DateOnly firstSession, DateOnly lastSession, HoldOutOptions options, SelectionOptions selection)
    {
        var months = lastSession < firstSession ? 0 : ((lastSession.DayNumber - firstSession.DayNumber) + 1) / DaysPerMonth;
        var plan = FromMonths(months, options, selection);
        var start = lastSession.AddMonths(-plan.LengthMonths).AddDays(1);
        return plan with { Start = start <= firstSession ? null : start, Status = start <= firstSession ? "no_history" : plan.Status };
    }
}
