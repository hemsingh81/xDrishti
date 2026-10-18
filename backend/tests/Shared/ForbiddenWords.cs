using System.Text.RegularExpressions;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The words a text of a P3 run must not contain (spec P3-04b1, AC 6; P3-04b2, D6): each is a word of a performance statistic, and a text that has one is one step from a claim. The match is whole-word and case-insensitive, so
/// <c>window</c>, <c>summary</c> and a symbol such as <c>TATACONSUM</c> are not hits; a digit followed by a space and <c>R</c> (a figure in R) is a hit too. One list, used by every slice that scans a text.
/// </summary>
internal static partial class ForbiddenWords
{
    public static readonly IReadOnlyList<string> List =
    [
        "win", "wins", "winner", "winners", "winning", "loss", "losses", "loser", "losers", "losing", "hit", "hits", "expectancy", "profit", "profits", "profitable",
        "drawdown", "average", "mean", "sum", "total", "percent", "percentage", "rate", "ratio",
    ];

    /// <summary>The forbidden words found in the text (each once, lower case, in the order of <see cref="List"/>) and <c>"&lt;digit&gt; R"</c> when a figure in R is there.</summary>
    public static IReadOnlyList<string> In(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var found = new List<string>();
        var words = new HashSet<string>(WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant()), StringComparer.Ordinal);
        found.AddRange(List.Where(words.Contains));
        if (DigitRPattern().IsMatch(text))
        {
            found.Add("<digit> R");
        }

        return found;
    }

    [GeneratedRegex(@"\b\w+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\d R\b", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRPattern();
}
