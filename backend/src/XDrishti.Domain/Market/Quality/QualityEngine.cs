using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// The data quality checks of one instrument (design doc 05 §10b, ADR 0017 §4): completeness, bar sanity, spikes, aggregates against the 1-minute bars, freshness, and the official daily comparison
/// (not available). Pure: everything it needs is in the <see cref="QualityInput"/>, nothing is read or written here.
/// </summary>
public static class QualityEngine
{
    public static QualityOutcome Evaluate(QualityInput input, QualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (input.Sessions.Count == 0)
        {
            throw new ArgumentException("The window has no session.", nameof(input));
        }

        var bounds = QualityWindow.Bounds(input.Sessions[0]);
        var windowBars = WindowBars(input);
        var (completeness, missing) = CompletenessChecker.Check(input.Calendar, input.Sessions, input.FirstBar, input.Bars, input.UnfillableDays, thresholds.MaxMissingPercent);
        var sanity = SanityChecker.Check(windowBars);
        var spikes = SpikeChecker.Check(windowBars, thresholds.SpikeMultiple);
        var (aggregates, differences) = AggregateChecker.Summarize(AggregateChecker.Compare(input.Bars, input.Stored, bounds));
        var freshness = FreshnessChecker.Check(input.Calendar, input.Now, input.LastBar);

        var checks = new QualityChecks(completeness, sanity, spikes, aggregates, freshness, OfficialDailyCheck.NotAvailable, new QualityDetails(missing, differences));
        return new QualityOutcome(QualityScoring.Status(checks), QualityScoring.Score(checks), windowBars.Count, completeness.MissingMinutes, checks);
    }

    /// <summary>The bars that lie on a session of the window, in time order.</summary>
    private static List<Bar> WindowBars(QualityInput input)
    {
        var days = input.Sessions.ToHashSet();
        return [.. input.Bars.Where(bar => days.Contains(SessionClock.DateOf(bar.EpochSeconds)))];
    }
}
