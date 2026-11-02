using System.Globalization;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Hand-built populations for protocol F (P15-10): a trade list with exact counts per season and an exact R, no random numbers.</summary>
public static class FilterKit
{
    public static DateOnly Day(int season, int k) => new DateOnly(2023 + (season / 4), ((season % 4) * 3) + 1, 1).AddDays(k % 80);

    /// <summary>For each of <paramref name="seasons"/> seasons: the given numbers of trades per set. Skipped and taken tails are R -2; the other R is chosen so that the means are equal when <paramref name="equalMeans"/>.</summary>
    public static List<FilterTrade> Population(int seasons, int skipped, int taken, int skippedTail, int takenTail, bool equalMeans, int noUpcoming = 0, int noEntry = 0, int onlyCancelled = 0, double skippedOther = 0.5)
    {
        var list = new List<FilterTrade>();
        var takenOther = 0.5;
        var takenMean = (((taken - takenTail) * takenOther) - (2.0 * takenTail)) / taken;
        var skippedFill = equalMeans ? (((takenMean * skipped) + (2.0 * skippedTail)) / (skipped - skippedTail)) : skippedOther;
        for (var s = 0; s < seasons; s++)
        {
            for (var i = 0; i < skipped; i++)
            {
                list.Add(new FilterTrade(Key("s", s, i), Day(s, i), i < skippedTail ? -2.0 : skippedFill, FilterFate.Skipped));
            }

            for (var i = 0; i < taken; i++)
            {
                list.Add(new FilterTrade(Key("t", s, i), Day(s, i), i < takenTail ? -2.0 : takenOther, FilterFate.Taken));
            }

            Unavailable(list, s, "n", noUpcoming, "no_upcoming_entry");
            Unavailable(list, s, "e", noEntry, "no_entry");
            Unavailable(list, s, "c", onlyCancelled, "only_cancelled");
        }

        return list;
    }

    private static void Unavailable(List<FilterTrade> list, int season, string tag, int count, string reason)
    {
        for (var i = 0; i < count; i++)
        {
            list.Add(new FilterTrade(Key(tag, season, i), Day(season, i), 0.1, FilterFate.Unavailable, reason));
        }
    }

    private static string Key(string tag, int season, int i) => string.Create(CultureInfo.InvariantCulture, $"{tag}|{season}|{i}");
}
