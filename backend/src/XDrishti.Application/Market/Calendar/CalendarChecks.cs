using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Calendar;

/// <summary>Checks shared by the calendar use cases.</summary>
internal static class CalendarChecks
{
    /// <summary>Times a change is tried when the database refuses it because of a concurrent change (it reads the day again each time).</summary>
    public const int Attempts = 3;

    /// <summary>The longest span <c>GET /api/data/calendar</c> answers.</summary>
    public const int MaxSpanDays = 3660;

    /// <summary>How far ahead a day may be added.</summary>
    public const int MaxYearsAhead = 5;

    public static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary><c>HH:mm</c> to minutes since midnight.</summary>
    public static bool TryClock(string? text, out int minute)
    {
        minute = 0;
        if (!TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        minute = time.Hour * 60 + time.Minute;
        return true;
    }

    public static Result<DateOnly> RouteDate(string? text) =>
        TryDate(text, out var date) ? Result.Success(date) : Result.Failure<DateOnly>(DataErrors.Invalid("date", "Use the date as yyyy-MM-dd."));
}
