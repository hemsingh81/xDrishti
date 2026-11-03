namespace XDrishti.Domain.Learning.Extensions;

/// <summary>A family in use without a current KEEP (P15-12): reported, never blocked.</summary>
public sealed record EventsViolation(string Family, string Text, long? DecisionId, string? DecidedBy);
