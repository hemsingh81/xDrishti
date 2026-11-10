namespace XDrishti.Domain.Reporting;

/// <summary>One daily traded volume of an instrument (BK-06).</summary>
/// <param name="Date">The India date of the session.</param>
/// <param name="Volume">The stored volume in shares.</param>
public sealed record DailyVolume(DateOnly Date, double Volume);
