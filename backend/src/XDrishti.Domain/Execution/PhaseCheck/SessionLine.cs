namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The per-session line (criterion 8). Runs read <c>readable</c>, <c>unreadable</c> or <c>missing</c>; <see cref="MedianState"/> is <c>value</c> or <c>insufficient</c>.</summary>
public sealed record SessionLine(
    DateOnly Date, string Class, IReadOnlyList<string> Reasons, int OrdersSent, int SandboxIntents, TicketTally Tickets, string EodRun, string FinalRun, int Breaks, IReadOnlyList<string> BreakKinds,
    IReadOnlyList<string> Alarms, IReadOnlyList<string> Halts, string DayState, double? MedianMinutes, string MedianState);
