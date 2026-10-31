namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>
/// One Trading account as the records show it. <see cref="Mode"/> is the effective mode at the end of the window ("semi_auto" or any other name, including "unknown"); <see cref="WindowStart"/> is the session date of the newest promotion to
/// semi_auto, null when there never was one. <see cref="Sessions"/> are the trading days from the window start to the end, ascending.
/// </summary>
public sealed record AccountPhaseFacts(
    Guid AccountId, string Name, string Mode, DateOnly? WindowStart, IReadOnlyList<SessionFacts> Sessions, bool SessionsTruncated, IReadOnlyList<DateOnly> Demotions, AccountExtras Extras);
