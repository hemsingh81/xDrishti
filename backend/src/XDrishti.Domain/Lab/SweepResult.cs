namespace XDrishti.Domain.Lab;

/// <summary>The result of one sweep point or of the base: the trades of a plain run and their figures, or the reason the engine refused it (<see cref="Error"/>).</summary>
public sealed record SweepResult(SweepPoint Point, int Trades, double? MeanR, double? ProfitFactor, double? TotalR, string? Error);
