namespace XDrishti.Domain.Market.Context;

/// <summary>A day (or a VIX leg, for <see cref="ContextSkipReason.VixThin"/>) the derivation did not use, with the reason and the number of 1-minute bars it had (0 when it had no row).</summary>
public sealed record SkippedContextDay(DateOnly Date, ContextSkipReason Reason, int Bars);
