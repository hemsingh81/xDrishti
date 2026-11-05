namespace XDrishti.Domain.Lab;

/// <summary>One placebo shape in the ranking (CF-07, D2).</summary>
/// <param name="Shape">The shape name.</param>
/// <param name="Placed">Real trades that got a placebo trade.</param>
/// <param name="MeanR">Mean net R of the placebo trades; null when none.</param>
/// <param name="Rank">Its place among the real strategy and the comparable placebos, 1 for the best; null when it is not comparable. A tie ranks the real strategy after the placebo.</param>
/// <param name="Comparable">True when enough trades were placed for it to take part in the ranking.</param>
/// <param name="Reason">Why it is not comparable; null when it is.</param>
public sealed record PlaceboRow(string Shape, int Placed, double? MeanR, int? Rank, bool Comparable, string? Reason);
