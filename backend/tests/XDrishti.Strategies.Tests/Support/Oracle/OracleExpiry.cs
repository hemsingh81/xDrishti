namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>Seeded expiry dates for the oracle and look-ahead tests, covering the dates of a series.</summary>
internal static class OracleExpiry
{
    /// <summary>The Thursdays of a long stretch around the series and a few other dates, as a set the frame may share.</summary>
    public static IReadOnlySet<DateOnly> ExpiryDays(int seed)
    {
        var random = new Random(seed * 31 + 5);
        var days = new HashSet<DateOnly>();
        for (var i = 0; i < 400; i++)
        {
            var date = Clock.Mon.AddDays(i - 40);
            if (date.DayOfWeek == DayOfWeek.Thursday || random.Next(100) < 4)
            {
                days.Add(date);
            }
        }

        return days;
    }
}
