namespace XDrishti.Application.Confidence;

/// <summary>
/// The forward (paper edge) series of one strategy in the figures the scorecard needs (spec CF-11 D2): how many trades, the effective n after the design effect of the days' candidates, the mean and spread of the net R, and the months. Where the series is gross of charges
/// (the cost sheet is not confirmed) the mean, the spread and the months are null, because the effect must be net: a gross figure is never offered as one.
/// </summary>
/// <param name="Trades">Graded trades in the series.</param>
/// <param name="NEff">The effective n.</param>
/// <param name="MeanR">The mean net R, or null (fewer than 2 trades, or a gross series).</param>
/// <param name="SdR">The sample standard deviation of the net R, or null.</param>
/// <param name="PositiveMonthShare">The share of months with at least 5 trades whose mean is above 0, or null when no month qualifies or the series is gross.</param>
/// <param name="Gross">Whether the series is before charges.</param>
/// <param name="Basis">The series basis in words.</param>
internal sealed record ForwardEdge(int Trades, double NEff, double? MeanR, double? SdR, double? PositiveMonthShare, bool Gross, string Basis)
{
    /// <summary>The standard error of the mean, <c>sd / sqrt(n_eff)</c>, or null.</summary>
    public double? StandardError => SdR is { } sd && NEff > 0 ? sd / Math.Sqrt(NEff) : null;
}
