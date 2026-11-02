namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The outcome of protocol P, the variant chosen on the first seasons (frozen before the test is read; null when nothing was chosen) and the sign of the test gain that the hold-out must repeat.</summary>
public sealed record ProtocolPResult(ProtocolOutcome Outcome, string? ChosenVariant, double? TestGainR);
