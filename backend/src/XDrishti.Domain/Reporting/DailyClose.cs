namespace XDrishti.Domain.Reporting;

/// <summary>One daily close of an instrument (P8-07).</summary>
/// <param name="Date">The India date of the session.</param>
/// <param name="Close">The stored close.</param>
public sealed record DailyClose(DateOnly Date, double Close);
