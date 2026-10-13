namespace XDrishti.Domain.Platform.Services;

/// <summary>Helpers for <see cref="Weekdays"/> and the three-letter codes of the wire format (mon … sun).</summary>
public static class WeekdaySets
{
    private static readonly (DayOfWeek Day, Weekdays Flag, string Code, string Name)[] All =
    [
        (DayOfWeek.Monday, Weekdays.Monday, "mon", "Monday"),
        (DayOfWeek.Tuesday, Weekdays.Tuesday, "tue", "Tuesday"),
        (DayOfWeek.Wednesday, Weekdays.Wednesday, "wed", "Wednesday"),
        (DayOfWeek.Thursday, Weekdays.Thursday, "thu", "Thursday"),
        (DayOfWeek.Friday, Weekdays.Friday, "fri", "Friday"),
        (DayOfWeek.Saturday, Weekdays.Saturday, "sat", "Saturday"),
        (DayOfWeek.Sunday, Weekdays.Sunday, "sun", "Sunday"),
    ];

    public static Weekdays Of(DayOfWeek day) => All.First(a => a.Day == day).Flag;

    public static bool Includes(this Weekdays set, DayOfWeek day) => (set & Of(day)) != Weekdays.None;

    /// <summary>The codes of the days in the set, Monday first.</summary>
    public static IReadOnlyList<string> ToCodes(this Weekdays set) => [.. All.Where(a => (set & a.Flag) != Weekdays.None).Select(a => a.Code)];

    /// <summary>The set of the codes; false (and the set so far) when one is unknown. A null list is the empty set.</summary>
    public static bool TryParse(IEnumerable<string>? codes, out Weekdays set)
    {
        set = Weekdays.None;
        foreach (var code in codes ?? [])
        {
            var found = All.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));
            if (found.Code is null)
            {
                return false;
            }

            set |= found.Flag;
        }

        return true;
    }

    public static bool TryParseDay(string? code, out DayOfWeek day)
    {
        var found = All.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));
        day = found.Day;
        return found.Code is not null;
    }

    public static string CodeOf(DayOfWeek day) => All.First(a => a.Day == day).Code;

    public static string NameOf(DayOfWeek day) => All.First(a => a.Day == day).Name;

    /// <summary>The days in words: "Monday to Friday", "every day", "Monday, Wednesday".</summary>
    public static string Describe(this Weekdays set)
    {
        if (set == Weekdays.EveryDay)
        {
            return "every day";
        }

        if (set == Weekdays.MondayToFriday)
        {
            return "Monday to Friday";
        }

        return string.Join(", ", All.Where(a => (set & a.Flag) != Weekdays.None).Select(a => a.Name));
    }
}
