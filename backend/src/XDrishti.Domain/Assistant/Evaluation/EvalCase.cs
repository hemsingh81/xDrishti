namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One data question of the eval set (spec P9-09 "The 30 data questions").</summary>
/// <param name="Id">DQ-01 to DQ-30.</param>
/// <param name="Category">kpi, groups, portfolio, plan, learning or honest.</param>
/// <param name="Question">The text put to the assistant.</param>
/// <param name="Parts">What a correct answer must contain; all of them.</param>
/// <param name="Anchors">Words that mark a sentence as the one that answers; a figure in such a sentence that is not an expected value is a conflict.</param>
/// <param name="AllowedNumbers">Numbers that may appear in an answer without being wrong or ungrounded.</param>
/// <param name="ToolsExpected">The tools a good answer is expected to use (reported, never gated).</param>
public sealed record EvalCase(string Id, string Category, string Question, IReadOnlyList<ExpectedPart> Parts, IReadOnlyList<string> Anchors, IReadOnlyList<EvalFraction> AllowedNumbers, IReadOnlyList<string> ToolsExpected)
{
    /// <summary>An honest-state case: a figure the answer must not give.</summary>
    public bool IsHonest => Parts.Any(p => p is HonestPart);
}
