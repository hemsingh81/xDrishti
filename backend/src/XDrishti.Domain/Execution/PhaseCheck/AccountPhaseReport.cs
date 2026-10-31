namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The verdict of one account with its numbers. <see cref="FirstMetAt"/> is the date the streak first reached the need (a later break does not rewrite it); <see cref="LastBreak"/> names the date and the reasons of the last broken session.</summary>
public sealed record AccountPhaseReport(
    Guid AccountId, string Name, DateOnly? WindowStart, string Mode, string Verdict, int Streak, int Counted, int Idle, int Broken, int OutOfMode, int SandboxSessions, DateOnly? FirstMetAt, string? LastBreak,
    IReadOnlyList<SessionLine> Sessions, IReadOnlyList<PhaseFact> Facts, string Label);
