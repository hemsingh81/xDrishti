namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>Counts read beside the verdict (spec P12-12 criterion 9); they are printed with their n and never scored. A null count is a source that could not be read.</summary>
public sealed record AccountExtras(
    int? LiveLegs, int? LiveLegsWithAlgoId, int? LiveEntryFills, int ForeignBreaks, int DrillsInWindow, DateOnly? LastDrillDate, bool? LastDrillPassed, int NotReadyDays, DateOnly? CanaryC1, DateOnly? CanaryC2)
{
    public static AccountExtras Unread { get; } = new(null, null, null, 0, 0, null, null, 0, null, null);
}
