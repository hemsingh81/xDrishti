namespace XDrishti.Domain.Paper;

/// <summary>A verdict on one exit statistic with its label (spec P10-04, criteria 8).</summary>
/// <param name="Verdict"><c>met</c>, <c>not_met</c> or <c>not_evaluable</c>.</param>
/// <param name="Label">The fact in words, with the numbers.</param>
public sealed record CriterionResult(string Verdict, string Label)
{
    /// <summary>The statistic holds.</summary>
    public const string Met = "met";

    /// <summary>The statistic does not hold.</summary>
    public const string NotMet = "not_met";

    /// <summary>The data do not allow a verdict.</summary>
    public const string NotEvaluable = "not_evaluable";
}
