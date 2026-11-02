namespace XDrishti.Domain.Filings;

/// <summary>
/// The version of the results parser and its hard limits (P15-03, ADR 0184). The version is <c>xbrl-1-synthetic</c> until the owner's five real filings pass criterion 1; only then is it <c>xbrl-1</c>. Every consumer carries it, so a number parsed by a parser that was never
/// checked against a real filing is visibly that.
/// </summary>
public static class ResultsParser
{
    public const string Version = "xbrl-1-synthetic";

    /// <summary>Whether facts of this parser version have passed real filings: only a known, non-synthetic version is verified. A missing or empty version is not, so a consumer labels what it builds on it.</summary>
    public static bool IsVerified(string? version) => !string.IsNullOrWhiteSpace(version) && !version.EndsWith("-synthetic", StringComparison.Ordinal);

    public const int MaxDepth = 64;

    public const int MaxElements = 200_000;

    /// <summary>The longest element text the parser keeps; a mapped fact with a longer text is <c>bad_value</c>.</summary>
    public const int MaxLeafText = 64 * 1024;
}
