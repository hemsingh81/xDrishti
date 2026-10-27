namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One thing an answer must contain (spec P9-09, format of <c>data-questions.jsonl</c>); a case with several parts needs all of them.</summary>
public abstract record ExpectedPart;

/// <summary>A number: the answer must print it with at least <paramref name="MinDecimals"/> decimals, equal to the exact value rounded half away from zero to the printed decimals (no tolerance band).</summary>
public sealed record NumberPart(EvalFraction Value, int MinDecimals, string? Unit) : ExpectedPart;

/// <summary>A count: the answer must print exactly this integer.</summary>
public sealed record IntegerPart(long Value) : ExpectedPart;

/// <summary>A word: the answer must contain one of <paramref name="ContainsAny"/> (whole words, case-insensitive) and none of <paramref name="Forbid"/>.</summary>
public sealed record TextPart(IReadOnlyList<string> ContainsAny, IReadOnlyList<string> Forbid) : ExpectedPart;

/// <summary>An honest state: the answer must say, with one of <paramref name="Phrases"/>, that the figure is not there, and give no number for the <paramref name="Withheld"/> metric.</summary>
public sealed record HonestPart(string State, IReadOnlyList<string> Phrases, string? Withheld) : ExpectedPart;
