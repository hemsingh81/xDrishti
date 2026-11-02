namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The result of a block bootstrap (P13-05). A bound is null (unavailable) when there were fewer than two blocks; never a value.</summary>
/// <param name="Point">The difference of the data itself.</param>
/// <param name="Lower">The lower one-sided bound, or null.</param>
/// <param name="Upper">The upper one-sided bound, or null.</param>
/// <param name="StandardError">The standard deviation of the draws, or null.</param>
/// <param name="Blocks">The number of blocks.</param>
/// <param name="Draws">The number of draws.</param>
public sealed record BootstrapBounds(double? Point, double? Lower, double? Upper, double? StandardError, int Blocks, int Draws);
