namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// What a frame is told about the calendar in the weekly and monthly tests: nothing; every special session and holiday; the holidays and the weekday sessions only (a Saturday or Sunday session is then a surprise);
/// or the holidays only (a weekend session is a surprise, and a weekday that ends early or misses its last minute is closed late by the next date, as the calendar says it is a regular session).
/// </summary>
public enum CalendarKnowledge
{
    None,
    Full,
    WeekdaysOnly,
    HolidaysOnly,
}
