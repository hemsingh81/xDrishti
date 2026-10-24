namespace XDrishti.Domain.Planning;

/// <summary>
/// What the pre-open check could read about one instrument (spec P7-11). A null part is "not available", never "none": the rules invalidate on it.
/// </summary>
/// <param name="Last">The last (indicative, before the open) price, rupees; null when no usable quote came.</param>
/// <param name="PreviousClose">The previous session's close, rupees; null when not given.</param>
/// <param name="Atr">The average true range of the last closed sessions, rupees; null when there were too few sessions.</param>
/// <param name="QuoteTime">The time of the last trade the price comes from (from the same quote answer); null when the answer had none, which reads as no price.</param>
public sealed record PreOpenReading(double? Last, double? PreviousClose, double? Atr, DateTimeOffset? QuoteTime = null);
