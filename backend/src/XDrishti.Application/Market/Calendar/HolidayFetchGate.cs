namespace XDrishti.Application.Market.Calendar;

/// <summary>Remembers on which India date NSE's holiday list was last tried, so the automatic attempt after data jobs happens once a day (also after a failure; the owner can ask again at any time).</summary>
public sealed class HolidayFetchGate
{
    private int _lastDay = int.MinValue;

    /// <summary>True the first time it is asked for a date (and remembers it); false for the rest of that day.</summary>
    public bool TryBegin(DateOnly today) => Interlocked.Exchange(ref _lastDay, today.DayNumber) != today.DayNumber;
}
