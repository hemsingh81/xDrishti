namespace XDrishti.Application.Confidence;

/// <summary>
/// The forward (paper edge) series of one strategy in the figures the scorecard needs (spec CF-11 D2, GROSS-01): how many trades, the effective n after the design effect of the days' candidates, the mean and spread of each trade's gross R less the cost buffer, and the months.
/// Nothing here is net: the figures carry the basis label <see cref="CostBasis"/>, such as <c>gross_less_buffer(0.10R)</c>.
/// </summary>
/// <param name="Trades">Graded trades in the series.</param>
/// <param name="NEff">The effective n.</param>
/// <param name="MeanR">The mean R on the basis, or null (fewer than 2 trades).</param>
/// <param name="SdR">The sample standard deviation of the R on the basis, or null.</param>
/// <param name="PositiveMonthShare">The share of months with at least 5 trades whose mean is above 0, or null when no month qualifies.</param>
/// <param name="CostBasis">The basis label, such as <c>gross_less_buffer(0.10R)</c>; a reference is compared only on the same label.</param>
/// <param name="Basis">The series basis in words.</param>
internal sealed record ForwardEdge(int Trades, double NEff, double? MeanR, double? SdR, double? PositiveMonthShare, string CostBasis, string Basis)
{
    /// <summary>The standard error of the mean, <c>sd / sqrt(n_eff)</c>, or null.</summary>
    public double? StandardError => SdR is { } sd && NEff > 0 ? sd / Math.Sqrt(NEff) : null;
}
