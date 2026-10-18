namespace XDrishti.Strategies.Syntax;

/// <summary>
/// Reads the text of a condition into a syntax tree, or into every problem it can find. Pure and thread-safe: no clock, no I/O, no state between calls. Limits keep hostile or runaway text harmless: a text longer
/// than <see cref="MaxLength"/> characters or nested deeper than <see cref="MaxNesting"/> levels is refused with a problem before any tree is built, so a deep expression can neither overflow the stack here nor in anything that
/// walks the tree later. Nothing is ever thrown for a bad text.
/// </summary>
/// <remarks>
/// <b>Stages.</b> (1) the length limit; (2) the lexer (Superpower) and the lexical problems: unknown characters, unterminated strings, numbers glued to letters, numbers out of range; (3) the nesting limit of brackets and prefix operators; (4) the grammar
/// (Superpower) with the recovery described on <see cref="Recovery"/>; (5) the nesting limit of the tree. The problems of all stages are returned together, in source order, at most <see cref="MaxProblems"/> of them. <b>Positions.</b> Offsets are UTF-16 code units from 0; lines and columns count from 1; lines
/// end at <c>\n</c>. A span that is empty (the end of the text, an empty text) has <c>Start == End</c>.
/// </remarks>
public static class ConditionParser
{
    /// <summary>The longest text accepted, in characters.</summary>
    public const int MaxLength = 4000;

    /// <summary>
    /// The deepest nesting accepted (see <see cref="Nesting"/>): the brackets open at once plus the prefix operators in a run, and, separately, the depth of the syntax tree (every call, prefix operator, subscript and
    /// operator of a chain is one level; parentheses add none).
    /// </summary>
    public const int MaxNesting = 64;

    /// <summary>The most problems a result lists (see <see cref="SyntaxCodes.TooManyProblems"/>): reading stops there, which bounds the work and the size of the answer for any text of at most <see cref="MaxLength"/> characters.</summary>
    public const int MaxProblems = Recovery.MaxProblems;

    /// <summary>Parses the text of one condition.</summary>
    public static ParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaxLength)
        {
            return Failed(new Problem(
                SyntaxCodes.TooLong,
                "The condition is " + source.Length + " characters long; the limit is " + MaxLength + ". The part beyond the limit is marked.",
                SourceText.SpanOf(source, MaxLength, source.Length)));
        }

        var (tokens, problems) = LexicalPass.Run(source);
        if (tokens.Count == 0 && problems.Count == 0)
        {
            return Failed(new Problem(SyntaxCodes.Empty, "The condition is empty; write a condition such as rsi(14) > 55.", SourceText.SpanOf(source, 0, source.Length)));
        }

        if (Nesting.FirstTooDeep(tokens) is { } tooDeep)
        {
            problems.Add(new Problem(
                SyntaxCodes.TooDeep,
                "The condition is nested too deeply: more than " + MaxNesting + " levels of brackets and prefix operators. Split it into simpler conditions.",
                Grammar.SpanOf(tooDeep)));
            return Failed(problems, source);
        }

        try
        {
            var tree = Recovery.Run(source, tokens, problems, out var outcome);
            if (problems.Count == 0 && tree is not null && Nesting.Depth(tree, MaxNesting) > MaxNesting)
            {
                problems.Add(new Problem(
                    SyntaxCodes.TooDeep,
                    "The condition is nested too deeply: its structure is more than " + MaxNesting + " levels deep. Every function call, `not`, minus sign and subscript, and every operator in a row (`and`, `or`, `+`, `-`, `*`, `/`), adds one level,"
                        + " so about 60 conditions joined by `and` is the most one condition can hold. Split it into several conditions.",
                    tree.Span));
                return Failed(problems, source);
            }

            return problems.Count == 0 && tree is not null ? new ParseResult(tree, []) : Failed(problems, source, outcome.Truncated);
        }
        catch (InsufficientExecutionStackException)
        {
            // The nesting limit keeps the parser well inside the stack; this is for a caller that is already deep in its own stack.
            problems.Add(new Problem(SyntaxCodes.TooDeep, "The condition cannot be read here: there is not enough stack left for its nesting.", SourceText.SpanOf(source, 0, 0)));
            return Failed(problems, source);
        }
    }

    private static ParseResult Failed(Problem problem) => new(null, [problem]);

    private static ParseResult Failed(List<Problem> problems, string source, bool truncated = false)
    {
        // Source order; problems at one place keep the order they were found in (the lexical ones come first).
        var ordered = problems.Select((problem, index) => (problem, index)).OrderBy(p => p.problem.Span.Start).ThenBy(p => p.index).Select(p => p.problem).ToArray();
        if (ordered.Length == 0)
        {
            return new ParseResult(null, [new Problem(SyntaxCodes.UnexpectedToken, "The condition could not be read.", SourceText.SpanOf(string.Empty, 0, 0))]);
        }

        if (ordered.Length <= MaxProblems && !truncated)
        {
            return new ParseResult(null, ordered);
        }

        // More than can be listed: the first MaxProblems found, in the order of the text (the lexical problems are found before the syntax ones, an unclosed bracket after the problems inside it), and one problem that says
        // so, for the text after them. The note does not quote a limit: the recovery may also stop because it ran out of parses, with only a problem or two listed.
        var listed = ordered.Length > MaxProblems ? ordered[..MaxProblems] : ordered;
        var rest = SourceText.SpanOf(source, Math.Min(listed[^1].Span.End, source.Length), source.Length);
        var more = new Problem(
            SyntaxCodes.TooManyProblems,
            "There are more problems than are listed: the first " + listed.Length + " are shown. Fix them and read the condition again to see the rest.",
            rest);
        return new ParseResult(null, [.. listed, more]);
    }
}
