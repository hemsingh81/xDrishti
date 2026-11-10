namespace XDrishti.Domain.Reporting;

/// <summary>The basket index on days that followed one regime code (BK-06 m16).</summary>
/// <param name="Code">The regime code of the previous session.</param>
/// <param name="Days">Days in the group.</param>
/// <param name="MeanDailyReturn">Mean daily index return (fraction), or insufficient.</param>
/// <param name="AnnualVolatility">Annualised volatility (fraction).</param>
/// <param name="HitRate">Share of days the basket return beat the benchmark return.</param>
public sealed record RegimeRow(int Code, int Days, Metric MeanDailyReturn, Metric AnnualVolatility, Metric HitRate);
