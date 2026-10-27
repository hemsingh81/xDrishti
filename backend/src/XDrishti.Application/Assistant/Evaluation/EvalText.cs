using System.Text.RegularExpressions;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>Normalises an answer so that two repeats can be compared for stability (spec P9-09 AC 8): lower case, white space collapsed. Nothing else is changed, so a different number is a different answer.</summary>
internal static partial class EvalText
{
    public static string Key(string text) => White().Replace(text.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex White();
}
