namespace XDrishti.Domain.Research;

/// <summary>What one (signal, shape) pair gave: the net R of its placebo trade, or why there is none (no bar, no fill, a walk refused).</summary>
/// <param name="SignalId"><see cref="PlaceboSignal.Id"/>.</param>
/// <param name="Shape">The position of the shape in <see cref="PlaceboShapes.All"/>.</param>
/// <param name="NetR">Net R of the placebo trade after charges; null when no trade was made.</param>
/// <param name="Reason">Why there is no trade; null when there is one.</param>
public sealed record PlaceboOutcome(int SignalId, int Shape, double? NetR, string? Reason);
