namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>All the stored facts the check judges (spec P12-12). <see cref="Needed"/> is 30; only tests pass another number.</summary>
public sealed record ExecutionPhaseFacts(DateOnly Through, IReadOnlyList<AccountPhaseFacts> Accounts, BoundaryFacts Boundary, int Needed = ExecutionPhaseCheck.RequiredSessions);
