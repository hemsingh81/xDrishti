namespace XDrishti.Domain.Reporting;

/// <summary>One point of the relative strength line.</summary>
/// <param name="Date">The session.</param>
/// <param name="Value">RS, 100 at the first common date.</param>
public sealed record RelativeStrengthPoint(DateOnly Date, double Value);
