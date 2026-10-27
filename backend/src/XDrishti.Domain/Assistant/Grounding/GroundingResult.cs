namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>The outcome of the check: the status, how many figures were looked at and the ones that were not found. The check never changes the text.</summary>
public sealed record GroundingResult(GroundingStatus Status, int Checked, IReadOnlyList<UngroundedSpan> Ungrounded)
{
    /// <summary>The result of a turn that was cancelled or failed.</summary>
    public static GroundingResult NotChecked { get; } = new(GroundingStatus.NotChecked, 0, []);
}
