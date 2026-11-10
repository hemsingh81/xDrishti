namespace XDrishti.Domain.Reporting;

/// <summary>How the basket index moves against the benchmark (BK-06 m5 to m7).</summary>
/// <param name="Figures">One per beta window.</param>
/// <param name="RollingBeta">The rolling beta series.</param>
/// <param name="Capture">Up and down capture.</param>
/// <param name="CommonDays">Days both series have a return.</param>
public sealed record BenchmarkRelation(IReadOnlyList<BetaFigure> Figures, IReadOnlyList<RollingBetaPoint> RollingBeta, CaptureResult Capture, int CommonDays);
