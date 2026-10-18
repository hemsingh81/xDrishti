namespace XDrishti.Strategies.Compilation;

/// <summary>
/// Collects the problems of one strategy compile, in the order they are found (which is the order of the model), and caps them at the end: the first <see cref="Cap"/> errors, then the first <see cref="Cap"/> warnings and
/// information, one <c>too_many_problems</c> for each group that lost some. A model problem has no text, so its span is the start of nothing.
/// </summary>
internal sealed class ProblemSink
{
    /// <summary>The most errors listed, and the most warnings and notes listed.</summary>
    public const int Cap = 100;

    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    private readonly List<LocatedProblem> _errors = [];

    private readonly List<LocatedProblem> _others = [];

    /// <summary>True when an error was found (the compile fails).</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>How many errors were found so far (a step that compares before and after knows whether it found one).</summary>
    public int ErrorCount => _errors.Count;

    public void Error(string path, string code, string message, string? suggestion = null) =>
        _errors.Add(new LocatedProblem(path, new Problem(code, message, NoText, suggestion)));

    public void Warning(string path, string code, string message) =>
        _others.Add(new LocatedProblem(path, new Problem(code, message, NoText, null, ProblemSeverity.Warning)));

    /// <summary>A problem of a condition of the language, unchanged, with the path of its item.</summary>
    public void Condition(string path, Problem problem)
    {
        var located = new LocatedProblem(path, problem);
        if (problem.Severity == ProblemSeverity.Error)
        {
            _errors.Add(located);
        }
        else
        {
            _others.Add(located);
        }
    }

    /// <summary>The problems as returned: errors, then warnings and information, each group capped.</summary>
    public IReadOnlyList<LocatedProblem> Build()
    {
        var all = new List<LocatedProblem>(Math.Min(_errors.Count, Cap) + Math.Min(_others.Count, Cap) + 2);
        all.AddRange(_errors.Take(Cap));
        if (_errors.Count > Cap)
        {
            all.Add(new LocatedProblem(string.Empty, new Problem(
                ModelCodes.TooManyProblems,
                "There are " + _errors.Count + " errors; the first " + Cap + " are listed. Fix them and compile again to see the rest.",
                NoText)));
        }

        all.AddRange(_others.Take(Cap));
        if (_others.Count > Cap)
        {
            all.Add(new LocatedProblem(string.Empty, new Problem(
                ModelCodes.TooManyProblems,
                "There are " + _others.Count + " warnings and notes; the first " + Cap + " are listed.",
                NoText,
                null,
                ProblemSeverity.Information)));
        }

        return all.AsReadOnly();
    }
}
