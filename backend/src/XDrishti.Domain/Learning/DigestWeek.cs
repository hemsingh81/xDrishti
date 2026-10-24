using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Learning;

/// <summary>An ISO week (Monday to Sunday) as the digest's key (P7-15, ADR 0094): <c>2026-W43</c>. The digest is stored once per week.</summary>
public readonly record struct DigestWeek
{
    private DigestWeek(int year, int week)
    {
        Year = year;
        Week = week;
    }

    /// <summary>The ISO week-numbering year.</summary>
    public int Year { get; }

    /// <summary>The ISO week number, 1 to 53.</summary>
    public int Week { get; }

    /// <summary>The key, <c>2026-W43</c>.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-W{Week:D2}");

    /// <summary>The Monday of the week.</summary>
    public DateOnly Start => DateOnly.FromDateTime(ISOWeek.ToDateTime(Year, Week, DayOfWeek.Monday));

    /// <summary>The Sunday of the week.</summary>
    public DateOnly End => Start.AddDays(6);

    /// <summary>The week a date falls in.</summary>
    public static DigestWeek Of(DateOnly date) => new(ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)), ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)));

    /// <summary>Reads <c>YYYY-Www</c>; null when it is not a week that exists.</summary>
    public static DigestWeek? TryParse(string? text)
    {
        if (text is not { Length: 8 } || text[4] != '-' || text[5] != 'W'
            || !int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(text.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var week)
            || year is < 2000 or > 2100 || week < 1 || week > ISOWeek.GetWeeksInYear(year))
        {
            return null;
        }

        return new DigestWeek(year, week);
    }

    /// <summary>
    /// Why a digest of this week may not be STORED at <paramref name="now"/>, or null when it may: only the current week, from its Saturday 10:00 India time (the weekly service's time), or the most recent completed week, may be stored; the digest reads
    /// today's state, so an older week stored immutably would show today's state as that week's. An older week can only be built as a dry run.
    /// </summary>
    public string? StoreRefusal(DateTimeOffset now)
    {
        var today = IndiaTime.Today(now);
        if (this != Of(today) && this != Of(today.AddDays(-7)))
        {
            return "Only the current week or the most recent completed week can be stored; an older or future week can only be built as a dry run, because the digest reads today's state.";
        }

        var saturday = new DateTimeOffset(Start.AddDays(5).ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset);
        return now < saturday ? "A week is stored from its Saturday 10:00 India time; before that the digest would be near empty and could not be replaced." : null;
    }

    /// <inheritdoc />
    public override string ToString() => Key;
}
