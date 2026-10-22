namespace XDrishti.Domain.Learning;

/// <summary>The "always take" baseline of a test window (P6-05): every candidate trade is taken, no model.</summary>
/// <param name="N">Trades.</param>
/// <param name="WinRate">Share with label 1.</param>
/// <param name="MeanR">Mean result at resolution in R.</param>
public sealed record AlwaysTakeSummary(int N, double WinRate, double MeanR);
