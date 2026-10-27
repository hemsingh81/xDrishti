namespace XDrishti.Domain.Assistant;

/// <summary>What <see cref="Calc.Evaluate"/> answers: the value as text, or a code of the tool contract (<c>calc.rejected</c>, <c>calc.divide_by_zero</c>) with a one-word reason for the log (never the expression).</summary>
public sealed record CalcResult(string? Text, string? ErrorCode, string? Reason)
{
    public bool IsSuccess => ErrorCode is null;
}
