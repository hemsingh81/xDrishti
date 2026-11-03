namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// The buckets of the half-spread model (spec P14-07): moneyness by strike steps from the snapshot's ATM (<c>0</c>, <c>1-2</c>, <c>3-5</c>, <c>6-10</c>), days to expiry (<c>0-2</c>, <c>3-7</c>, <c>8-30</c>, <c>31+</c>) and time of day in India time
/// (<c>09:20-10:00</c>, <c>10:00-14:30</c>, <c>14:30-15:30</c>). <see cref="All"/> is the merged word of a split that was merged. A quote outside every bucket (more than ten steps out, before 09:20 or after 15:30) is in none and is not used.
/// </summary>
public static class SpreadBuckets
{
    /// <summary>The word of a split that was merged.</summary>
    public const string All = "all";

    private static readonly string[] MoneynessWords = ["0", "1-2", "3-5", "6-10"];

    private static readonly string[] DteWords = ["0-2", "3-7", "8-30", "31+"];

    private static readonly string[] TodWords = ["09:20-10:00", "10:00-14:30", "14:30-15:30"];

    public static IReadOnlyList<string> Moneyness => MoneynessWords;

    public static IReadOnlyList<string> Dte => DteWords;

    public static IReadOnlyList<string> Tod => TodWords;

    /// <summary>The position of a moneyness word among the four; -1 for any other word.</summary>
    public static int MoneynessIndex(string word) => Array.IndexOf(MoneynessWords, word);

    /// <summary>The position of a days-to-expiry word among the four; -1 for any other word.</summary>
    public static int DteIndex(string word) => Array.IndexOf(DteWords, word);

    /// <summary>The position of a time-of-day word among the three; -1 for any other word.</summary>
    public static int TodIndex(string word) => Array.IndexOf(TodWords, word);

    /// <summary>The moneyness word of a distance in strike steps; null beyond ten steps or below zero.</summary>
    public static string? MoneynessOf(int strikeSteps) => strikeSteps switch
    {
        < 0 => null,
        0 => MoneynessWords[0],
        <= 2 => MoneynessWords[1],
        <= 5 => MoneynessWords[2],
        <= 10 => MoneynessWords[3],
        _ => null,
    };

    /// <summary>The days-to-expiry word; null for a negative number of days (an expiry in the past).</summary>
    public static string? DteOf(int daysToExpiry) => daysToExpiry switch
    {
        < 0 => null,
        <= 2 => Dte[0],
        <= 7 => Dte[1],
        <= 30 => Dte[2],
        _ => Dte[3],
    };

    /// <summary>The time-of-day word of an India clock time; null before 09:20 and after 15:30.</summary>
    public static string? TodOf(TimeOnly indiaTime)
    {
        if (indiaTime < new TimeOnly(9, 20) || indiaTime > new TimeOnly(15, 30))
        {
            return null;
        }

        return indiaTime < new TimeOnly(10, 0) ? Tod[0] : indiaTime < new TimeOnly(14, 30) ? Tod[1] : Tod[2];
    }

    /// <summary>The bucket of a leg, or null when any key is outside the buckets.</summary>
    public static SpreadBucket? Of(int strikeSteps, int daysToExpiry, TimeOnly indiaTime) =>
        MoneynessOf(strikeSteps) is { } m && DteOf(daysToExpiry) is { } d && TodOf(indiaTime) is { } t ? new SpreadBucket(m, d, t) : null;
}
