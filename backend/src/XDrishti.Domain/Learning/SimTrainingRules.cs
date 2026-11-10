using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The pure rules of training from the simulation (spec AU-13, ADR 0243 point 4, ADR 0249): the folds are the purged, anchored, quarterly walk-forward folds of ADR 0067 and nothing else; this class only converts the embargo from sessions to days, decides which
/// closed trades may enter at all (none that reaches the hold-out block) and names what is missing when there is too little to train on. No I/O, no clock.
/// </summary>
public static class SimTrainingRules
{
    /// <summary>The default embargo, in sessions (the same 7 calendar days as ADR 0067).</summary>
    public const int DefaultEmbargoSessions = 5;

    /// <summary>The largest embargo allowed, in sessions.</summary>
    public const int MaxEmbargoSessions = 20;

    /// <summary>The basis of the cell statistics this path writes; never mixed with the research basis.</summary>
    public const string Basis = "training_sim";

    /// <summary>
    /// Calendar days that cover <paramref name="sessions"/> trading sessions, from the trading calendar (holidays and special closures included): the longest span, over the job window, from the session that many sessions before a session to that session.
    /// With no holiday and a Monday-to-Friday week, 5 sessions are 7 days and 20 are 28; a holiday week is longer. The longest span is used for every fold, so the embargo is never shorter than the asked number of sessions.
    /// </summary>
    public static int EmbargoDays(int sessions, DateOnly from, DateOnly to, Func<DateOnly, bool> isSession)
    {
        ArgumentNullException.ThrowIfNull(isSession);
        if (sessions is < 0 or > MaxEmbargoSessions)
        {
            throw new ArgumentOutOfRangeException(nameof(sessions), sessions, "The embargo is 0 to 20 sessions.");
        }

        if (sessions == 0)
        {
            return 0;
        }

        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (isSession(d))
            {
                days.Add(d);
            }
        }

        var longest = 0;
        for (var i = sessions; i < days.Count; i++)
        {
            longest = Math.Max(longest, days[i].DayNumber - days[i - sessions].DayNumber);
        }

        return longest == 0 ? (sessions * 7 + 4) / 5 : longest; // no calendar information (window too short to hold more sessions than the embargo): sessions x 7/5 days, rounded up. Holidays are unknown here, so this is a floor that can be a day or two short; the calendar branch above is exact
    }

    /// <summary>
    /// True when a closed trade may be a row: it was decided inside the training window and its label horizon (its exit) ends before the hold-out block starts. A trade that reaches into the block is left out whole, so no label ever uses data of the block.
    /// </summary>
    public static bool Admits(DateOnly tradeDate, long exitEpochSeconds, DateOnly windowTo, DateOnly holdOutStart) =>
        tradeDate <= windowTo && SessionClock.DateOf(exitEpochSeconds) < holdOutStart;

    /// <summary>The validation knobs of the path: the doc 16 defaults of ADR 0067 with the embargo in calendar days (from <see cref="EmbargoDays"/>).</summary>
    public static ValidationOptions Options(int embargoDays, int trials) => new(EmbargoDays: embargoDays, Trials: Math.Max(1, trials));

    /// <summary>The first missing quantity as words, or null when there is enough to judge a model: rows, then usable folds.</summary>
    public static string? NotEvaluable(int rows, int minRows, int usableFolds, int minFolds, DateOnly? from, DateOnly? to)
    {
        var c = CultureInfo.InvariantCulture;
        if (rows < minRows)
        {
            return string.Create(c, $"rows: {rows} usable closed trades, {minRows} needed");
        }

        if (usableFolds < minFolds)
        {
            var span = from is { } f && to is { } t ? string.Create(c, $" (the history runs {f:yyyy-MM-dd} to {t:yyyy-MM-dd}; the first test fold starts 9 months in)") : string.Empty;
            return string.Create(c, $"walk-forward folds: {usableFolds} usable, {minFolds} needed{span}");
        }

        return null;
    }
}
