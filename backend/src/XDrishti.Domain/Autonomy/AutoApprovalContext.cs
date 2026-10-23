namespace XDrishti.Domain.Autonomy;

/// <summary>The moment and the running count that the auto-approval rule needs.</summary>
/// <param name="Now">The time of the decision.</param>
/// <param name="ReviewCutoff">The plan's review cutoff; an auto approval is only before it.</param>
/// <param name="AutoApprovedToday">How many auto approvals the planned session already has.</param>
public sealed record AutoApprovalContext(DateTimeOffset Now, DateTimeOffset ReviewCutoff, int AutoApprovedToday);
