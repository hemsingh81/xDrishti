namespace XDrishti.Domain.Learning;

/// <summary>Why a term of the influence predicate does not hold (CF-10 part 10c-2): a stable code and a sentence.</summary>
/// <param name="Code">A stable lower-case code such as <c>holdout_reused</c>.</param>
/// <param name="Text">A sentence for a person.</param>
public sealed record MlInfluenceReason(string Code, string Text);
