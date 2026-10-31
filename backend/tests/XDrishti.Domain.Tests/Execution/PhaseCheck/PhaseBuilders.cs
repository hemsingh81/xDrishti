using XDrishti.Domain.Execution.PhaseCheck;

namespace XDrishti.Domain.Tests.Execution.PhaseCheck;

/// <summary>Builders for the P12-12 tests: a clean counted session by default, changed with <c>with</c>.</summary>
internal static class PhaseBuilders
{
    public static readonly DateOnly Start = new(2026, 11, 2);

    public static SessionFacts Counted(DateOnly date) => new(
        date, true, false, 0, false, 1, 0, 0, false, [], new RunFact(true), new RunFact(true), [], [], "ready", TicketTally.None);

    public static SessionFacts Idle(DateOnly date) => Counted(date) with { LiveEntries = 0, Eod = null, Final = null };

    public static SessionFacts Broken(DateOnly date) => Counted(date) with { BreakKinds = ["position_mismatch"] };

    public static SessionFacts Out(DateOnly date) => Counted(date) with { InModeAtOpen = false };

    public static AccountPhaseFacts Account(IReadOnlyList<SessionFacts> sessions, string mode = ExecutionPhaseCheck.SemiAuto, DateOnly? windowStart = null) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Main", mode, windowStart ?? (sessions.Count > 0 ? sessions[0].Date : null), sessions, false, [], AccountExtras.Unread);

    public static BoundaryFacts CleanBoundary { get; } = new(["a"], ["a"], []);

    public static ExecutionPhaseReport Evaluate(IReadOnlyList<SessionFacts> sessions, int needed = 30, string mode = ExecutionPhaseCheck.SemiAuto) =>
        ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(sessions.Count == 0 ? Start : sessions[^1].Date, [Account(sessions, mode)], CleanBoundary, needed));

    public static List<SessionFacts> Run(int count, Func<DateOnly, SessionFacts> make) => [.. Enumerable.Range(0, count).Select(i => make(Start.AddDays(i)))];
}
