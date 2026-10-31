namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>An end-of-day reconciliation run that exists; <see cref="Readable"/> false is an unavailable run, which is never clean. A run that is missing is a null, not a RunFact.</summary>
public sealed record RunFact(bool Readable);
