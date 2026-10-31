namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>
/// The stored facts of one account on one trading day, already read (spec P12-12). <see cref="LiveEntries"/> counts entry legs sent in the live environment and <see cref="UnconfirmedEntries"/> those without a confirmed owner ticket at most 30 s before sending.
/// Sandbox intents are only counted, never judged. A run that is missing is null.
/// </summary>
public sealed record SessionFacts(
    DateOnly Date,
    bool InModeAtOpen,
    bool ModeChangedInSession,
    int DemotionsInSession,
    bool CanaryPending,
    int LiveEntries,
    int UnconfirmedEntries,
    int SandboxIntents,
    bool PositionCarriedIn,
    IReadOnlyList<string> BreakKinds,
    RunFact? Eod,
    RunFact? Final,
    IReadOnlyList<string> Alarms,
    IReadOnlyList<string> Halts,
    string? DayState,
    TicketTally Tickets);
