using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The grade of one answer (spec P9-09 AC 2 to 4).</summary>
/// <param name="CaseId">The question.</param>
/// <param name="Correct">Every expected part is there and no conflicting number stands in an anchored sentence.</param>
/// <param name="Grounded">Every figure of the answer is in the tool results, the question or the allowed numbers; null when no tool results were given (not evaluable).</param>
/// <param name="HardFail">An honest-state case with an ungrounded figure: a hard fail of the whole set.</param>
/// <param name="Codes">The failure codes, in the order found; empty for a pass.</param>
/// <param name="UngroundedFigures">The printed text of each ungrounded figure.</param>
public sealed record AnswerGrade(string CaseId, bool Correct, bool? Grounded, bool HardFail, ValueList<string> Codes, ValueList<string> UngroundedFigures)
{
    /// <summary>Correct and grounded: both are needed (spec AC 4).</summary>
    public bool Passed => Correct && Grounded == true;
}
