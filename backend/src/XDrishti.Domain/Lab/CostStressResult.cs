namespace XDrishti.Domain.Lab;

/// <summary>The cost stress of a set of out-of-sample trades (CF-05): the three points (1x, middle, high) and the worst of them, which is the "tradable" label.</summary>
/// <param name="Points">1x, the middle and the high multiplier, in that order.</param>
/// <param name="Tradable">The worst of the three: <see cref="StressStatus.Fail"/> when any is a miss, else <see cref="StressStatus.Unavailable"/> when any has no figure, else <see cref="StressStatus.Pass"/>.</param>
public sealed record CostStressResult(IReadOnlyList<StressPoint> Points, StressStatus Tradable);
