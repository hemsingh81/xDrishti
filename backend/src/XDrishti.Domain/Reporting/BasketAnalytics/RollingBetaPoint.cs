namespace XDrishti.Domain.Reporting;

/// <summary>One point of the rolling beta series; a window with no variance has no point.</summary>
/// <param name="Date">The last day of the window.</param>
/// <param name="Beta">The beta.</param>
public sealed record RollingBetaPoint(DateOnly Date, double Beta);
