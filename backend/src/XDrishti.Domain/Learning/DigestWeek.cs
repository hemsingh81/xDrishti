using System.Globalization;

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

    /// <inheritdoc />
    public override string ToString() => Key;
}
