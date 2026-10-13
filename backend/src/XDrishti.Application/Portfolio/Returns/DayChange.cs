namespace XDrishti.Application.Portfolio.Returns;

/// <summary>The day change of one holding: the amount in rupees and the percentage (a percent number: 1.5 is 1.5 %) of the previous close.</summary>
public sealed record HoldingDayChange(decimal Amount, decimal Pct);

/// <summary>
/// The day change of holdings together. <see cref="Amount"/> and <see cref="Pct"/> are null when no holding is included. <see cref="Included"/> holdings count in it; <see cref="Excluded"/> open holdings are left out
/// because they have no previous close (or no usable price): they are never counted as zero.
/// </summary>
public sealed record GroupDayChange(decimal? Amount, decimal? Pct, int Included, int Excluded);
