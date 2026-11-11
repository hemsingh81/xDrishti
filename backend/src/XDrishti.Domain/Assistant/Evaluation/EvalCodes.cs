namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The failure codes of the answer grader (spec P9-09 AC 2 to 4). They are stored in reports, so they never change.</summary>
public static class EvalCodes
{
    public const string EmptyAnswer = "empty_answer";
    public const string MissingNumber = "missing_number";
    public const string WrongNumber = "wrong_number";
    public const string TooFewDecimals = "too_few_decimals";
    public const string ConflictingNumber = "conflicting_number";
    public const string MissingText = "missing_text";
    public const string ForbiddenText = "forbidden_text";
    public const string HonestStateMissing = "honest_state_missing";
    public const string WithheldNumberGiven = "withheld_number_given";
    public const string UngroundedNumber = "ungrounded_number";
    public const string GroundingNotEvaluable = "grounding_not_evaluable";

    /// <summary>The tool answered <c>tool.unavailable</c> (the basket reports of BK-12b with no API behind them): the question could not be asked of real data, so it is not evaluable and never a pass.</summary>
    public const string ToolNotEvaluable = "tool_not_evaluable";
}
