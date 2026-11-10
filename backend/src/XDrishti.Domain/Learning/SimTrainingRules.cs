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

    /// <summary>Calendar days that cover <paramref name="sessions"/> trading sessions: five sessions a week, rounded up (5 sessions = 7 days, 20 = 28).</summary>
    public static int EmbargoDays(int sessions)
    {
        if (sessions is < 0 or > MaxEmbargoSessions)
        {
            throw new ArgumentOutOfRangeException(nameof(sessions), sessions, "The embargo is 0 to 20 sessions.");
        }

        return (sessions * 7 + 4) / 5;
    }

    /// <summary>
    /// True when a closed trade may be a row: it was decided inside the training window and its label horizon (its exit) ends before the hold-out block starts. A trade that reaches into the block is left out whole, so no label ever uses data of the block.
    /// </summary>
    public static bool Admits(DateOnly tradeDate, long exitEpochSeconds, DateOnly windowTo, DateOnly holdOutStart) =>
        tradeDate <= windowTo && SessionClock.DateOf(exitEpochSeconds) < holdOutStart;

    /// <summary>The validation knobs of the path: the doc 16 defaults of ADR 0067 with the embargo from the sessions asked for.</summary>
    public static ValidationOptions Options(int embargoSessions, int trials) => new(EmbargoDays: EmbargoDays(embargoSessions), Trials: Math.Max(1, trials));

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
