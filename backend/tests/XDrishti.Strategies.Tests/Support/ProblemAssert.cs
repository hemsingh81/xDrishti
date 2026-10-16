namespace XDrishti.Strategies.Tests.Support;

/// <summary>Compares the problems of a compile with the expected ones: the same number, in the same order, with the code, the severity, the exact span (offsets, line, column) and the suggestion; and the message when it is pinned.</summary>
internal static class ProblemAssert
{
    public static void Match(string source, IReadOnlyList<Problem> actual, params Expected[] expected)
    {
        var shown = string.Join("\n", actual.Select(p => $"  {p.Severity} {p.Code} [{p.Span.Start},{p.Span.End}) {p.Message}"));
        actual.Count.ShouldBe(expected.Length, "the problems of " + source + " were:\n" + shown);
        for (var i = 0; i < expected.Length; i++)
        {
            var (start, end) = expected[i].Locate(source);
            var (line, column) = SourceLocator.At(source, start);
            var problem = actual[i];
            var where = source + " problem " + i + " (" + expected[i].Code + ")";

            problem.Code.ShouldBe(expected[i].Code, where);
            problem.Severity.ShouldBe(expected[i].Severity, where);
            (problem.Span.Start, problem.Span.End).ShouldBe((start, end), where + " span; the problem covers `" + source[problem.Span.Start..problem.Span.End] + "`");
            (problem.Span.Line, problem.Span.Column).ShouldBe((line, column), where + " line and column");
            problem.Suggestion.ShouldBe(expected[i].Suggestion, where + " suggestion");
            if (expected[i].Message is { } message)
            {
                problem.Message.ShouldBe(message, where + " message");
            }
        }
    }
}
