namespace XDrishti.Domain.Lab;

/// <summary>What the placebo walk gave for one shape: how many of the real trades got a placebo trade and their mean net R (null when none did).</summary>
/// <param name="Name">The shape name (<see cref="Research.PlaceboShapes"/>).</param>
/// <param name="Placed">Real trades that got a placebo trade of this shape.</param>
/// <param name="MeanR">Mean net R of those placebo trades after the same charges; null when <paramref name="Placed"/> is 0.</param>
/// <param name="NotPlacedReason">Why no trade could be placed when that is known before the walk (a roll onto the signal's own instrument); null otherwise.</param>
public sealed record PlaceboReading(string Name, int Placed, double? MeanR, string? NotPlacedReason = null);
