namespace XDrishti.SimOracle;

/// <summary>
/// One candidate of the oracle's table (spec P4-05, D5 to D7): its own sample, the sample on the common set, the mean difference from the first candidate on the common entries (NaN on the first row and when there is none) and the figures of the
/// MAE/MFE diagnostics, in the order of the spec: the shares of winners with an adverse excursion above 0.8R and 1.2R, the median MFE, the shares reaching 1R, 2R and 3R, the median giveback of the trades that reached 1R, and the share of those that gave back more than half.
/// </summary>
/// <param name="Name">The candidate.</param>
/// <param name="Own">On its own trades.</param>
/// <param name="Common">On the entries taken under every candidate.</param>
/// <param name="PairedMeanDifference">Mean of (result minus the first candidate's result) on the common entries.</param>
/// <param name="DipAbove08">Winners with MAE above 0.8R, as a share of the winners.</param>
/// <param name="DipAbove12">Winners with MAE above 1.2R, as a share of the winners.</param>
/// <param name="MedianMfe">Median MFE.</param>
/// <param name="Reach1">Share of trades with MFE of at least 1R.</param>
/// <param name="Reach2">Share with MFE of at least 2R.</param>
/// <param name="Reach3">Share with MFE of at least 3R.</param>
/// <param name="MedianGiveback">Median of MFE minus the exact result over the trades with MFE of at least 1R.</param>
/// <param name="GaveBackHalf">Share of those trades that gave back more than half of their MFE.</param>
public sealed record OracleComparisonRow(
    string Name,
    OracleSample Own,
    OracleSample Common,
    double PairedMeanDifference,
    double DipAbove08,
    double DipAbove12,
    double MedianMfe,
    double Reach1,
    double Reach2,
    double Reach3,
    double MedianGiveback,
    double GaveBackHalf);
