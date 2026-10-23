namespace XDrishti.Domain.Selection;

/// <summary>A daily close-to-close return of one instrument.</summary>
/// <param name="Date">The session.</param>
/// <param name="Value">The return (fraction).</param>
public sealed record DailyReturn(DateOnly Date, double Value);
