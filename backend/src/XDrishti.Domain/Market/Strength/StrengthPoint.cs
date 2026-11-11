namespace XDrishti.Domain.Market.Strength;

/// <summary>One stored relative-strength number of one instrument as the engine reads it (BK-09): the feature name, its parameters (basket code, lookback, skip, or the window), the last closed session it is as of and the value (NaN when the stored state is not a value).</summary>
public sealed record StrengthPoint(string Feature, int P1, int P2, int P3, DateOnly AsOf, double Value);
