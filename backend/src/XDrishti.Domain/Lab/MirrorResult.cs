namespace XDrishti.Domain.Lab;

/// <summary>The mirror gate of CF-07 (D3): the net expectancy of the real trades taken the other way round, with the same costs. A positive mirror reads "mostly drift".</summary>
/// <param name="Trades">Real trades that got a mirror trade.</param>
/// <param name="MirrorMeanR">Mean net R of the mirror trades; null when unavailable.</param>
/// <param name="RealMeanR">Mean net R of the real trades, beside it.</param>
/// <param name="Threshold">0: the mirror must earn at most this.</param>
/// <param name="Status">Pass (at most 0), fail (above 0) or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
public sealed record MirrorResult(int Trades, double? MirrorMeanR, double RealMeanR, double Threshold, NullStatus Status, string Reason);
