namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// One problem a condition must produce. The span is given as the text it covers (<paramref name="At"/>, the <paramref name="Nth"/> occurrence of it in the source, counted from 0), so the offsets are found by
/// searching the source and not by the library; the line and column are worked out by <see cref="SourceLocator"/>. <paramref name="Message"/> is the exact text when the case pins it (null: not compared).
/// </summary>
internal sealed record Expected(string Code, string At, string? Suggestion = null, string? Message = null, ProblemSeverity Severity = ProblemSeverity.Error, int Nth = 0)
{
    public static Expected Warning(string code, string at, string? suggestion = null, string? message = null, int nth = 0) => new(code, at, suggestion, message, ProblemSeverity.Warning, nth);

    public static Expected Info(string code, string at, string? message = null, int nth = 0) => new(code, at, null, message, ProblemSeverity.Information, nth);

    public (int Start, int End) Locate(string source)
    {
        var start = -1;
        for (var i = 0; i <= Nth; i++)
        {
            start = source.IndexOf(At, start + 1, StringComparison.Ordinal);
            start.ShouldBeGreaterThanOrEqualTo(0, "the test expects " + At + " (occurrence " + Nth + ") in " + source);
        }

        return (start, start + At.Length);
    }
}
