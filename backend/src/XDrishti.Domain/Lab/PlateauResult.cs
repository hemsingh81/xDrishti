namespace XDrishti.Domain.Lab;

/// <summary>
/// The plateau reading of one swept parameter (CF-07, D4). <see cref="Reading"/> is <c>plateau</c>, <c>spike</c>, <c>no_base_edge</c> or <c>inconclusive</c>; <see cref="Status"/> is pass only for <c>plateau</c>, fail for <c>spike</c> and <c>no_base_edge</c>,
/// unavailable for <c>inconclusive</c> (never a pass). The centre is reported, not applied.
/// </summary>
/// <param name="Parameter">The parameter name.</param>
/// <param name="Declared">The value the strategy declares.</param>
/// <param name="Points">All points in value order, the declared value included.</param>
/// <param name="ValidPoints">How many of them are valid.</param>
/// <param name="BaseMeanR">Net expectancy at the declared value; null when the base run gave none.</param>
/// <param name="LowerValue">The point just below the declared value; null when there is none.</param>
/// <param name="LowerRetention">Its mean over the base mean; null when it cannot be read.</param>
/// <param name="UpperValue">The point just above the declared value; null when there is none.</param>
/// <param name="UpperRetention">Its mean over the base mean; null when it cannot be read.</param>
/// <param name="Median">Median net expectancy over all valid points; null when there is none.</param>
/// <param name="CentreValue">The value maximizing the mean of itself and its two neighbours; null when no valid point has two valid neighbours.</param>
/// <param name="CentreMeanR">That smoothed mean.</param>
/// <param name="Reading">plateau, spike, no_base_edge or inconclusive.</param>
/// <param name="Status">Pass, fail or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
public sealed record PlateauResult(
    string Parameter,
    double Declared,
    IReadOnlyList<PlateauPoint> Points,
    int ValidPoints,
    double? BaseMeanR,
    double? LowerValue,
    double? LowerRetention,
    double? UpperValue,
    double? UpperRetention,
    double? Median,
    double? CentreValue,
    double? CentreMeanR,
    string Reading,
    NullStatus Status,
    string Reason);
