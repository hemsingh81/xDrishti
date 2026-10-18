namespace XDrishti.Domain.Research;

/// <summary>A flagged day (spec P3-04 Scope 6): the first open of the date over the last close of the previous session that ran, on the adjusted series. The day gets no entry and starts a new segment.</summary>
/// <param name="Date">The flagged date.</param>
/// <param name="Ratio">The first open over the previous close.</param>
/// <param name="ExcludedPositions">The swing positions that were open at the end of the segment before the day and were excluded because their walk could not be followed across it (ADR 0040, D13); 0 for an intraday strategy.</param>
public sealed record DiscontinuityNote(DateOnly Date, double Ratio, int ExcludedPositions = 0);
