namespace XDrishti.SimOracle;

/// <summary>A trading calendar: weekdays are sessions, holidays are not, special sessions (any weekday or a Saturday) are with their own window.</summary>
public sealed class OracleCalendar
{
    private readonly HashSet<DateOnly> _holidays;
    private readonly Dictionary<DateOnly, OracleSession> _special;

    /// <summary>Creates a calendar from its exceptions.</summary>
    public OracleCalendar(IEnumerable<DateOnly> holidays, IReadOnlyDictionary<DateOnly, OracleSession> specialSessions)
    {
        _holidays = [.. holidays];
        _special = new Dictionary<DateOnly, OracleSession>(specialSessions);
    }

    /// <summary>The default week: every weekday is a regular session.</summary>
    public static OracleCalendar Default { get; } = new([], new Dictionary<DateOnly, OracleSession>());

    /// <summary>The holidays.</summary>
    public IReadOnlyCollection<DateOnly> Holidays => _holidays;

    /// <summary>The special sessions.</summary>
    public IReadOnlyDictionary<DateOnly, OracleSession> SpecialSessions => _special;

    /// <summary>True when the date is a session.</summary>
    public bool IsTradingDay(DateOnly date)
    {
        if (_special.ContainsKey(date))
        {
            return true;
        }

        if (_holidays.Contains(date))
        {
            return false;
        }

        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <summary>The window of a date: its special window, else the regular one.</summary>
    public OracleSession SessionOf(DateOnly date) =>
        _special.TryGetValue(date, out OracleSession? s) ? s : OracleSession.Regular;

    /// <summary>The first trading day after a date within 30 calendar days.</summary>
    public DateOnly NextTradingDay(DateOnly date)
    {
        for (int i = 1; i <= 30; i++)
        {
            DateOnly c = date.AddDays(i);
            if (IsTradingDay(c))
            {
                return c;
            }
        }

        throw new InvalidOperationException("no trading day in 30 calendar days");
    }

    /// <summary>Moves forward a number of trading days.</summary>
    public DateOnly AddTradingDays(DateOnly date, int count)
    {
        DateOnly d = date;
        for (int i = 0; i < count; i++)
        {
            d = NextTradingDay(d);
        }

        return d;
    }

    /// <summary>The number of trading days d with from &lt; d &lt;= to.</summary>
    public int TradingDaysBetween(DateOnly from, DateOnly to)
    {
        int n = 0;
        for (DateOnly d = from.AddDays(1); d <= to; d = d.AddDays(1))
        {
            if (IsTradingDay(d))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The due minute of a session: the minute that starts at ToMinute - 1.</summary>
    public int DueMinute(DateOnly date) => SessionOf(date).ToMinute - 1;
}
