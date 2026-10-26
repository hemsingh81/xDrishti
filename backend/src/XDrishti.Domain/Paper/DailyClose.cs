namespace XDrishti.Domain.Paper;

/// <summary>The newest stored daily bar of an instrument that a mark reads.</summary>
/// <param name="Date">The India date of the bar.</param>
/// <param name="Price">The close.</param>
/// <param name="Complete">Whether the day bar is complete.</param>
public sealed record DailyClose(DateOnly Date, decimal Price, bool Complete);
