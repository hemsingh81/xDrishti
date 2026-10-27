namespace XDrishti.Domain.Assistant;

/// <summary>What <see cref="Calc.Evaluate"/> answers: the value as text, or a code of the tool contract (<c>calc.rejected</c>, <c>calc.divide_by_zero</c>) with a one-word reason for the log (never the expression). <see cref="Operands"/> are the number literals the expression held, as decimal text: the grounding side accepts the result only when each of them came from an earlier tool result.</summary>
public sealed record CalcResult(string? Text, string? ErrorCode, string? Reason, IReadOnlyList<string>? Operands = null)
{
    public bool IsSuccess => ErrorCode is null;
}
