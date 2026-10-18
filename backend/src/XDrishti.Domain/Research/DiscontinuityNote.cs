namespace XDrishti.Domain.Research;

/// <summary>A flagged day (spec P3-04 Scope 6): the first open of the date over the last close of the previous session that ran, on the adjusted series. Informational; the day gets no entry and starts a new segment.</summary>
public sealed record DiscontinuityNote(DateOnly Date, double Ratio);
