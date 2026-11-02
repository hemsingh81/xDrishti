namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The raw (unadjusted) close of one session of an instrument, the last minute bar of its India date (P15-05).</summary>
public readonly record struct DailyClose(DateOnly Date, double Close);
