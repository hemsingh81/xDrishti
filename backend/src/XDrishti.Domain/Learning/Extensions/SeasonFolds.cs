namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The season arithmetic of the P15 protocols (P15-10): the distinct seasons of some dates, the last four reserved as the hold-out, and the PEAD split into choice, test and hold-out seasons. Pure.</summary>
public static class SeasonFolds
{
    /// <summary>The number of seasons reserved as the hold-out.</summary>
    public const int HoldOutSeasons = 4;

    /// <summary>The distinct seasons of the dates, oldest first.</summary>
    public static IReadOnlyList<Season> Distinct(IEnumerable<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        return [.. dates.Select(Season.Of).Distinct().Order()];
    }

    /// <summary>The first day of the hold-out: the start of the fourth-last season with data, or null when there are not more than four seasons (nothing is left for development).</summary>
    public static DateOnly? HoldOutStart(IReadOnlyList<Season> seasons)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        return seasons.Count > HoldOutSeasons ? seasons[^HoldOutSeasons].Start : null;
    }

    /// <summary>The split of the seasons with events (oldest first): the first <paramref name="choice"/> choose, the last <see cref="HoldOutSeasons"/> are the hold-out and the seasons between are the test. With too few seasons the test is short or empty.</summary>
    public static (IReadOnlyList<Season> Choice, IReadOnlyList<Season> Test, IReadOnlyList<Season> HoldOut) Split(IReadOnlyList<Season> seasons, int choice)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        var hold = Math.Min(HoldOutSeasons, seasons.Count);
        var first = Math.Min(choice, seasons.Count - hold);
        return (seasons.Take(first).ToArray(), seasons.Skip(first).Take(seasons.Count - hold - first).ToArray(), seasons.Skip(seasons.Count - hold).ToArray());
    }
}
