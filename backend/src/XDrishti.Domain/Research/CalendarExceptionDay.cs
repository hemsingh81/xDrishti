namespace XDrishti.Domain.Research;

/// <summary>
/// A date on which the trading calendar departs from the default week (every weekday a regular 09:15 to 15:30 session): a holiday on a weekday, a special session (any window other than the regular one), or any session on a
/// weekend. <see cref="FromMinute"/> and <see cref="ToMinute"/> are minutes of the India day, 0 for a holiday.
/// </summary>
public sealed record CalendarExceptionDay(DateOnly Date, bool IsHoliday, int FromMinute, int ToMinute);
