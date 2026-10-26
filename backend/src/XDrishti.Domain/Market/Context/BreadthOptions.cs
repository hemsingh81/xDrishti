namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The definition and the thresholds of the breadth figure (P3-01e): the length of the simple average a close is compared with (<paramref name="SmaSessions"/>, 20 by the definition), the fewest voters for a number
/// (<paramref name="MinVoters"/>, 50: the standard error of a percentage stays under about 7 points) and the least share of the shares that have a bar on the day that must vote (<paramref name="MinShare"/>, 0.5).
/// </summary>
public sealed record BreadthOptions(int SmaSessions = 20, int MinVoters = 50, double MinShare = 0.5);
