namespace XDrishti.Domain.Research;

/// <summary>A range of India dates, both ends included.</summary>
public sealed record DateSpan(DateOnly First, DateOnly Last);
