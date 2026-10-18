namespace XDrishti.Domain.Research;

/// <summary>How many instruments of a run were skipped for one reason.</summary>
public sealed record SkipCount(SkipReason Reason, int Instruments);
