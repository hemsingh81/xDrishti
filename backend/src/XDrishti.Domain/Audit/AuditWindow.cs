using XDrishti.Domain.Market;

namespace XDrishti.Domain.Audit;

/// <summary>The days a request covers, India dates inclusive (default: today minus 29 to today; at most 366 days). Pure.</summary>
public readonly record struct AuditWindow(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;
    public const int DefaultDays = 30;

    /// <summary>The first instant (midnight in India of <see cref="From"/>).</summary>
    public DateTimeOffset Start => new(From.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    /// <summary>The instant after the last day (midnight in India after <see cref="To"/>); the window is open at this end.</summary>
    public DateTimeOffset End => new(To.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    public static AuditWindow Default(DateTimeOffset now)
    {
        var today = IndiaTime.Today(now);
        return new AuditWindow(today.AddDays(1 - DefaultDays), today);
    }

    /// <summary>Null when the days are usable: from not after to, and at most 366 days.</summary>
    public static string? Problem(DateOnly from, DateOnly to) =>
        from > to ? "The start is after the end." : to.DayNumber - from.DayNumber + 1 > MaxDays ? $"A range is at most {MaxDays} days." : null;
}
