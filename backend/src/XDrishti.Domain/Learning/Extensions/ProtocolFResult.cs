namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The outcome of protocol F and the test that carried it (<c>mean</c>, <c>tail</c>), which the hold-out repeats.</summary>
public sealed record ProtocolFResult(ProtocolOutcome Outcome, string Basis);
