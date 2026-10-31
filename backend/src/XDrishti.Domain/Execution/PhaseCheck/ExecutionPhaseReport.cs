namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The report (spec P12-12 Contract). <see cref="ExitVerdict"/> is the bare word, <see cref="ExitCriterion"/> the labelled line; <see cref="Boundary"/> is a printed fact that does not change a streak.</summary>
public sealed record ExecutionPhaseReport(
    DateOnly Through, string ExitVerdict, string ExitCriterion, int Needed, IReadOnlyList<AccountPhaseReport> Accounts, PhaseFact Boundary, IReadOnlyList<string> Limits);
