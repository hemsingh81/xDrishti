namespace XDrishti.Domain.Paper;

/// <summary>A final fact about an armed allocation that produced no entry (spec P10-01, criterion 7): the code from <see cref="SkipCodes"/> and a sentence.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="PlanItemId">The ticket.</param>
/// <param name="AccountId">The account.</param>
/// <param name="Code">One of <see cref="SkipCodes.All"/>.</param>
/// <param name="Detail">Why, in words (at most 500 characters).</param>
public sealed record PaperSkip(long PlanId, long PlanItemId, Guid AccountId, string Code, string Detail);
