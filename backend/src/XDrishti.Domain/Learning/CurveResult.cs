namespace XDrishti.Domain.Learning;

/// <summary>
/// The reading of a learning curve (spec CF-12, D5; ADR 0218).
/// </summary>
/// <param name="Verdict">The reading.</param>
/// <param name="PointsUsed">Points the line was fitted to (the last 3 to 5).</param>
/// <param name="SlopePerRow">The slope of the fitted line, gain per row; null with fewer than three points.</param>
/// <param name="SlopePer50Rows">The same per 50 rows.</param>
/// <param name="StandardError">The standard error of the slope per row; null with fewer than four points.</param>
/// <param name="ProjectedRows">The total rows at which the fitted line reaches the target gain; null unless the slope is above zero.</param>
/// <param name="MoreRows">Rows beyond the last point (0 when the line is already there); null unless the slope is above zero.</param>
/// <param name="Text">The words, with the numbers.</param>
public sealed record CurveResult(CurveVerdict Verdict, int PointsUsed, double? SlopePerRow, double? SlopePer50Rows, double? StandardError, double? ProjectedRows, double? MoreRows, string Text);
