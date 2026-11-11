namespace XDrishti.Domain.Learning;

/// <summary>One tracked share as of one decision date: how many daily bars it had before that date and the median of the last 20 sessions' traded value in rupees (null when it has fewer than 20 usable sessions).</summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Bars">Daily bars before the date.</param>
/// <param name="MedianTradedValue">Median of close x volume over the 20 sessions before the date, in rupees.</param>
public sealed record TierCandidate(Guid InstrumentId, int Bars, decimal? MedianTradedValue);
