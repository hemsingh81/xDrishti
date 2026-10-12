using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// The gate for the nightly plan (ADR 0017 §4), fail-closed and pure. It judges the latest FINISHED run over every instrument: a run that is still running is not a verdict, so it neither
/// hides a failed finished run (the gate stays blocked while the next run is underway) nor clears the gate by itself. Blocked when no finished run exists, the latest finished run failed, it is
/// older than <see cref="MaxRunAgeTradingDays"/> trading days (stale, per the calendar), or more than <c>maxFailedPercent</c> of the checked instruments fail (counted from the current results).
/// Not blocked but not ok when some instruments fail within the limit (the scan skips them individually).
/// </summary>
public static class QualityGateRules
{
    /// <summary>A finished run older than this many trading days no longer vouches for the data.</summary>
    public const int MaxRunAgeTradingDays = 3;

    /// <param name="counts">The counts of the current results.</param>
    /// <param name="lastFinishedFullRun">The latest finished (succeeded or failed) run over every instrument; null when there is none. A run that is still running is treated as none.</param>
    /// <param name="maxFailedPercent">How many percent of the checked instruments may fail before the gate blocks.</param>
    /// <param name="calendar">The trading calendar, for the age of the run in trading days.</param>
    /// <param name="now">Now.</param>
    public static QualityGateView Evaluate(QualityCounts counts, QualityRunView? lastFinishedFullRun, double maxFailedPercent, TradingCalendar calendar, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(calendar);
        var run = lastFinishedFullRun is { Status: "running" } ? null : lastFinishedFullRun;
        var percent = counts.Checked == 0 ? 0 : Math.Round(counts.Failed * 100.0 / counts.Checked, 1);
        var reasons = new List<string>();
        var blocked = false;
        if (run is null)
        {
            blocked = true;
            reasons.Add("There is no completed data quality run over every instrument yet.");
        }
        else if (run.Status == "failed")
        {
            blocked = true;
            reasons.Add("The last data quality run failed" + (string.IsNullOrWhiteSpace(run.Error) ? "." : ": " + run.Error));
        }
        else
        {
            var age = calendar.TradingDaysBetween(IndiaTime.Today(run.FinishedAt ?? run.StartedAt), calendar.LastClosedSession(now));
            if (age > MaxRunAgeTradingDays)
            {
                blocked = true;
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"The last completed data quality run is {age} trading days old (more than {MaxRunAgeTradingDays}); run the checks again."));
            }
        }

        if (counts.Checked > 0 && counts.Failed * 100.0 / counts.Checked > maxFailedPercent)
        {
            blocked = true;
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{counts.Failed} of {counts.Checked} instruments fail the checks ({percent} %), more than the {maxFailedPercent} % allowed."));
        }
        else if (counts.Failed > 0)
        {
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{counts.Failed} of {counts.Checked} instruments fail the checks ({percent} %); they are skipped, the limit is {maxFailedPercent} %."));
        }

        return new QualityGateView(!blocked && counts.Failed == 0, blocked, reasons, counts.Checked, counts.Failed, percent, maxFailedPercent, run?.FinishedAt ?? run?.StartedAt);
    }
}
