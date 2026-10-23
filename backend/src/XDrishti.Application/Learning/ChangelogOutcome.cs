namespace XDrishti.Application.Learning;

/// <summary>What recording a transition or a revert wrote (P6-08).</summary>
/// <param name="Id">The new row.</param>
/// <param name="Subject">The cell.</param>
/// <param name="From">The state before.</param>
/// <param name="To">The state after.</param>
public sealed record ChangelogOutcome(long Id, string Subject, string From, string To);
