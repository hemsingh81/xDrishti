namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// The word lists and limits of the guard (spec P9-06 AC 3 to 7), published here so a test pins them. The lists are heuristics, not a proof: a model can phrase a forecast without a listed word.
/// </summary>
public static class GuardRules
{
    public const int DailyMaxWords = 300;

    public const int WeeklyMaxWords = 400;

    /// <summary>A sentence may not start with one of these (case-insensitive): an instruction to the reader to trade.</summary>
    public static IReadOnlyList<string> ImperativeVerbs { get; } = ["buy", "sell", "short", "enter", "exit", "hold", "add", "average"];

    /// <summary>Phrases of a forecast or a promise (regular expressions, case-insensitive).</summary>
    public static IReadOnlyList<string> ForecastPatterns { get; } = [@"\bwill\s+(?:rise|fall|reach)\b", @"\bexpected\s+to\b", @"\blikely\s+to\b", @"\bguaranteed?\b", @"\bsure\b", @"\btarget\s+price\b", @"\bshould\s+go\b"];

    /// <summary>Number words that need the same word in the facts.</summary>
    public static IReadOnlyList<string> NumberWords { get; } =
    [
        "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
        "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety", "hundred", "thousand", "lakh", "crore", "half", "double", "twice",
    ];
}
