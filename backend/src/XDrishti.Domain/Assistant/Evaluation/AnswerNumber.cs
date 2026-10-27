namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>A figure found in an answer: where it is, its signed value, the decimals it was printed with and whether it carried a percent sign.</summary>
public sealed record AnswerNumber(int Start, int End, string Text, decimal Value, int Decimals, bool IsPercent);
