namespace XDrishti.Domain.Learning;

/// <summary>What is known about a model on the forward shadow candidates (CF-10 part 10c-2, spec D6). A figure that is null or not finite is not available.</summary>
/// <param name="GradedCandidates">Graded shadow candidates that this model scored.</param>
/// <param name="Weeks">Weeks they span.</param>
/// <param name="TopMinusBottomR">Mean R of the half the model ranks higher minus that of the other half.</param>
/// <param name="RollingEcePp">The rolling expected calibration error, in percentage points.</param>
public sealed record MlForwardFacts(int? GradedCandidates, double? Weeks, double? TopMinusBottomR, double? RollingEcePp);
