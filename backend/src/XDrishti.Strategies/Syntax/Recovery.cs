using Superpower.Model;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// Parses the tokens and, when the text is wrong, finds every problem it can instead of stopping at the first. A Superpower parser stops at its first failure, so the recovery wraps it in a loop: parse; at a failure,
/// explain it from the tokens around it (the code, the span and the message are decided here, from the position of the failure, the token before it and the brackets open there), then <b>repair the token list</b>
/// so that the same fault cannot fail again, and parse again. A repair does one of: insert a <see cref="Tk.Missing"/> value where one is due (an operator with nothing after it, an empty argument), delete the tokens up to
/// the next place where a fresh start is safe (the next <c>and</c>, <c>or</c> or comma at the same bracket level, or the end of the bracket), delete a whole subscript, replace a broken <c>tf(…)</c> by a missing
/// value, close the brackets still open at the end, or fix the kind of a wrong closing bracket. Each repair removes or supplies at least one token. The first problem is always exact; the
/// problems after it are those of the repaired text, which independent faults (a second unknown operator, a comma in the wrong place, a call and a comparison) report truthfully and dependent ones may report
/// as one problem or two. <b>One mistake, one problem:</b> a failure at a value the recovery itself supplied, a comparison
/// right after a value the recovery supplied, a <c>!</c> that follows a value (the lexical pass reported it and read it as <c>not</c>), a comma that was already reported as having no argument before it, and the end of a condition from which a repair has just removed the rest are consequences of a fault
/// that is already reported, so they are repaired without a second report. The tree is returned only when there was nothing to repair.
/// <para>
/// <b>Cost.</b> Every attempt parses the whole token list again (O(n) for n tokens, at most 4,000) and builds one array of the tokens, because Superpower reads an exact-length array; so a text with k problems costs
/// O(k·n). The recovery therefore gives up, and says so, after <see cref="MaxProblems"/> reported problems (the lexical ones counted) or <see cref="MaxAttempts"/> attempts, whichever comes first, which bounds the work to
/// a constant number of parses whatever the text: the worst texts of 4,000 characters measured (3,990 commas after <c>f(</c>, 4,000 closing brackets, a long sum followed by closing brackets …) take 13 to 52 ms and allocate 14 to 27 MB in a
/// Release build, where they took up to 0.7 s and 1.5 GB without the bound. The bound is checked by counting attempts (<see cref="RecoveryOutcome"/>), not by a clock.
/// </para>
/// </summary>
internal sealed class Recovery(string source, List<Token<Tk>> tokens, List<Problem> problems)
{
    /// <summary>The most problems a text is given: the recovery stops reporting at this many, and the parser adds one <see cref="SyntaxCodes.TooManyProblems"/> in place of the rest.</summary>
    public const int MaxProblems = 100;

    /// <summary>The most times the tokens are parsed: twice the problems, plus a margin for repairs that report nothing. A text with few tokens stops sooner (twice its tokens plus 33).</summary>
    public const int MaxAttempts = (2 * MaxProblems) + 32;

    private readonly Dictionary<int, int> _insertions = [];

    /// <summary>True while explaining a failure that is a consequence of a fault already reported (at a token the recovery itself supplied, right after a value it supplied, at a <c>!</c> the lexical pass reported, at the end a skip left): it is repaired without a second report.</summary>
    private bool _silent;

    /// <summary>True when the last repair deleted everything up to the end of the tokens: the text then seems to end early, which is not a fault of its own.</summary>
    private bool _skippedToEnd;

    private int _attempts;

    private bool _truncated;

    /// <summary>
    /// Parses; returns the tree if the text is valid (and <paramref name="problems"/> stays empty), else null with the syntax problems added to <paramref name="problems"/> (at most <see cref="MaxProblems"/> in all).
    /// The recovery takes the list of tokens over and repairs it in place: the caller must not use it afterwards.
    /// </summary>
    public static Expression? Run(string source, List<Token<Tk>> tokens, List<Problem> problems) => Run(source, tokens, problems, out _);

    /// <summary>The same, and also says how many times the tokens were parsed and whether the recovery gave up before the end of the text.</summary>
    public static Expression? Run(string source, List<Token<Tk>> tokens, List<Problem> problems, out RecoveryOutcome outcome)
    {
        var recovery = new Recovery(source, tokens, problems);
        var tree = recovery.Parse(problems.Count == 0);
        outcome = new RecoveryOutcome(recovery._attempts, recovery._truncated);
        return tree;
    }

    private Expression? Parse(bool clean)
    {
        var limit = Math.Min((2 * tokens.Count) + 33, MaxAttempts);
        while (_attempts < limit)
        {
            _attempts++;
            var result = Grammar.Condition(new TokenList<Tk>(tokens.ToArray()));
            if (result.HasValue)
            {
                return clean ? result.Value : null;
            }

            clean = false;
            if (!Explain(Math.Min(result.Remainder.Position, tokens.Count)) || _truncated)
            {
                return null;
            }
        }

        // Out of attempts. For a text with few tokens that cannot happen (every repair removes or supplies a token); for a long one it means more faults than are worth listing.
        _truncated = _attempts >= MaxAttempts;
        return null;
    }

    private Tk KindAt(int index) => index >= 0 && index < tokens.Count ? tokens[index].Kind : Tk.None;

    private string Shown(int index) => index >= tokens.Count ? "the end of the condition" : "`" + LexicalPass.Shorten(tokens[index].ToStringValue()) + "`";

    private SourceSpan SpanOfToken(int index) => Grammar.SpanOf(tokens[index]);

    /// <summary>The empty span just after the last token: where a condition that stops too soon stops.</summary>
    private SourceSpan EndSpan()
    {
        if (tokens.Count == 0)
        {
            return SourceText.SpanOf(source, 0, 0);
        }

        var last = Grammar.SpanOf(tokens[^1]);
        return new SourceSpan(last.End, last.End, last.Line, last.Column + last.Length);
    }

    private SourceSpan HereSpan(int index) => index >= tokens.Count ? EndSpan() : SpanOfToken(index);

    private void Report(string code, string message, SourceSpan span, string? suggestion = null)
    {
        if (_silent)
        {
            return;
        }

        if (problems.Count >= MaxProblems)
        {
            // One more than can be listed: stop here; the parser says that there are more.
            _truncated = true;
            return;
        }

        problems.Add(new Problem(code, message, span, suggestion));
    }

    /// <summary>A value that the recovery supplied itself (a zero-length <see cref="Tk.Missing"/>) stands at the index: whatever fails next to it is the same fault, already reported.</summary>
    private bool IsSupplied(int index) => index >= 0 && index < tokens.Count && tokens[index].Kind == Tk.Missing && tokens[index].Span.Length == 0;

    /// <summary>Indexes of the brackets open just before <paramref name="index"/>, outermost first.</summary>
    private List<int> OpenBefore(int index)
    {
        var open = new List<int>();
        for (var i = 0; i < index && i < tokens.Count; i++)
        {
            var kind = tokens[i].Kind;
            if (kind is Tk.LeftParen or Tk.LeftBracket)
            {
                open.Add(i);
            }
            else if (open.Count > 0 && ((kind == Tk.RightParen && KindAt(open[^1]) == Tk.LeftParen) || (kind == Tk.RightBracket && KindAt(open[^1]) == Tk.LeftBracket)))
            {
                open.RemoveAt(open.Count - 1);
            }
        }

        return open;
    }

    /// <summary>Explains the failure at <paramref name="at"/> and repairs the tokens; false when nothing could be repaired (the loop must stop).</summary>
    private bool Explain(int at)
    {
        var open = OpenBefore(at);
        var top = open.Count > 0 ? open[^1] : -1;
        var previous = at > 0 ? KindAt(at - 1) : Tk.None;
        var afterSkip = _skippedToEnd;
        _skippedToEnd = false;
        _silent = at < tokens.Count && tokens[at].Span.Length == 0;

        if (TryQualifier(at))
        {
            return true;
        }

        if (at >= tokens.Count)
        {
            return ExplainEnd(open, top, previous, afterSkip);
        }

        var kind = KindAt(at);
        if (top >= 0 && KindAt(top) == Tk.LeftBracket)
        {
            Report(
                SyntaxCodes.BadSubscript,
                "A subscript must be a whole number in digits, like close[1]; found " + Shown(at) + ".",
                SpanOfToken(at));
            DeleteGroup(top);
            return true;
        }

        if (previous == Tk.Dot && kind is not (Tk.Identifier or Tk.Missing))
        {
            Report(SyntaxCodes.UnexpectedToken, "A name is expected after `.`; found " + Shown(at) + ".", SpanOfToken(at));
            if (kind == Tk.Dot)
            {
                tokens.RemoveAt(at);
                return true;
            }

            return TokenRules.StartsOperand(kind) ? ReplaceWithMissing(at, at + 1) : InsertMissing(at);
        }

        switch (kind)
        {
            case Tk.RightParen or Tk.RightBracket:
                return ExplainClosing(at, top, previous);
            case Tk.Comma:
                return ExplainComma(at, top, previous);
            case Tk.Assign:
                Report(
                    SyntaxCodes.UnexpectedToken,
                    "A single `=` only names an argument (source=high); to compare two values write `==`.",
                    SpanOfToken(at),
                    "==");
                return SkipToResynchronisation(at);
            case Tk.Dot:
                return ExplainDot(at, previous);
            case Tk.LeftBracket:
                return ExplainSubscript(at, previous);
            case Tk.Not:
                if (TokenRules.EndsOperand(previous))
                {
                    // `close ! 5`: the lexical pass reported the `!` and read it as `not`; that a `not` cannot follow a value is the same mistake, and the text has no `not` to blame
                    _silent |= tokens[at].ToStringValue() != "not";
                    Report(SyntaxCodes.MissingOperator, "An operator (and, or, a comparison, +, -, *, /) is expected before " + Shown(at) + ".", SpanOfToken(at));
                    return SkipToResynchronisation(at);
                }

                Report(
                    SyntaxCodes.UnexpectedToken,
                    "`not` cannot be used here: it applies to a whole comparison, so write it in front of the comparison, like not (a < b).",
                    SpanOfToken(at));
                tokens.RemoveAt(at);
                return true;
            case var infix when TokenRules.IsInfix(infix):
                if (!TokenRules.EndsOperand(previous))
                {
                    Report(SyntaxCodes.MissingOperand, "A value is expected before " + Shown(at) + ".", SpanOfToken(at));
                    return InsertMissing(at);
                }

                if (TokenRules.IsComparison(infix))
                {
                    // `a <> b`: the value missing before `>` was reported, and the `>` that follows the value supplied for it is that same fault, not a second comparison.
                    _silent |= IsSupplied(at - 1);
                    Report(
                        SyntaxCodes.ChainedComparison,
                        "A comparison cannot be chained (" + Shown(at) + " follows another comparison); join the two with `and`, like a < b and b < c.",
                        SpanOfToken(at));
                    return SkipToResynchronisation(at);
                }

                break;
            case var start when TokenRules.StartsOperand(start):
                if (previous == Tk.Assign)
                {
                    Report(
                        SyntaxCodes.UnexpectedToken,
                        "A named argument takes a bare name, like source=high; found " + Shown(at) + ".",
                        SpanOfToken(at));
                    return ReplaceWithMissing(at, at + 1);
                }

                if (TokenRules.EndsOperand(previous))
                {
                    // After a value the parser was reported on already (an unreadable token), a second value is a consequence, not a new fault.
                    if (kind != Tk.Missing && !(previous == Tk.Missing && tokens[at - 1].Span.Length > 0))
                    {
                        Report(
                            SyntaxCodes.MissingOperator,
                            "An operator (and, or, a comparison, +, -, *, /) is expected before " + Shown(at) + ": two values cannot sit side by side.",
                            SpanOfToken(at));
                    }

                    return SkipToResynchronisation(at);
                }

                break;
        }

        Report(SyntaxCodes.UnexpectedToken, "Unexpected " + Shown(at) + ".", SpanOfToken(at));
        return SkipToResynchronisation(at);
    }

    private bool ExplainComma(int at, int top, Tk previous)
    {
        var inCall = top >= 0 && KindAt(top) == Tk.LeftParen && KindAt(top - 1) == Tk.Identifier;
        if (!inCall)
        {
            Report(SyntaxCodes.UnexpectedToken, "A comma only separates the arguments of a call, like rsi(14, source=high).", SpanOfToken(at));
            return SkipToResynchronisation(at);
        }

        if (previous is Tk.LeftParen or Tk.Comma)
        {
            Report(SyntaxCodes.MissingArgument, "An argument is missing before this comma.", SpanOfToken(at));
        }
        else
        {
            Report(SyntaxCodes.MissingOperand, "A value is expected before `,`.", SpanOfToken(at));
        }

        return InsertMissing(at);
    }

    private bool ExplainDot(int at, Tk previous)
    {
        if (TokenRules.NeedsOperandAfter(previous))
        {
            Report(SyntaxCodes.MissingOperand, "A value is expected before `.`.", SpanOfToken(at));
            var after = KindAt(at + 1);
            return ReplaceWithMissing(at, after is Tk.Identifier or Tk.Number or Tk.Missing ? at + 2 : at + 1);
        }

        Report(
            SyntaxCodes.UnexpectedToken,
            previous == Tk.Number
                ? "Unexpected `.` after a number; write a decimal number with digits on both sides, like 0.5."
                : "Unexpected `.`: a name has at most one dot (prev_day.high), or follows tf(\"1h\").",
            SpanOfToken(at));
        return SkipToResynchronisation(at);
    }

    private bool ExplainSubscript(int at, Tk previous)
    {
        if (TokenRules.NeedsOperandAfter(previous))
        {
            Report(SyntaxCodes.MissingOperand, "A value is expected before `[`.", SpanOfToken(at));
            return ReplaceWithMissing(at, MatchingClose(at) + 1);
        }

        if (previous != Tk.Missing || tokens[at - 1].Span.Length == 0)
        {
            Report(
                SyntaxCodes.SubscriptOnNonReference,
                previous == Tk.RightBracket
                    ? "A value can have only one subscript."
                    : "A subscript can only follow a name or a feature call, like close[1] or highest(20)[1]; it cannot follow a parenthesised expression or a literal.",
                SpanOfToken(at));
        }

        DeleteGroup(at);
        return true;
    }

    private bool ExplainClosing(int at, int top, Tk previous)
    {
        var closer = KindAt(at);
        if (top < 0)
        {
            Report(SyntaxCodes.UnmatchedClosingBracket, Shown(at) + " closes nothing: there is no open bracket before it.", SpanOfToken(at));
            tokens.RemoveAt(at);
            return true;
        }

        var wanted = KindAt(top) == Tk.LeftParen ? Tk.RightParen : Tk.RightBracket;
        if (closer != wanted)
        {
            Report(
                SyntaxCodes.UnmatchedClosingBracket,
                Shown(at) + " does not match the " + Shown(top) + " that is open; expected `" + (wanted == Tk.RightParen ? ")" : "]") + "`.",
                SpanOfToken(at),
                wanted == Tk.RightParen ? ")" : "]");
            tokens[at] = new Token<Tk>(wanted, tokens[at].Span);
            return true;
        }

        if (previous == Tk.Comma)
        {
            // `f(,)`: the comma was already reported as having no argument before it; it is not a trailing comma as well.
            _silent |= IsSupplied(at - 2);
            Report(SyntaxCodes.TrailingComma, "Remove the comma after the last argument.", SpanOfToken(at - 1));
            tokens.RemoveAt(at - 1);
            return true;
        }

        if (TokenRules.NeedsOperandAfter(previous))
        {
            Report(
                SyntaxCodes.MissingOperand,
                previous == Tk.Assign ? "A name is expected after `=`, like source=high; found " + Shown(at) + "." : "A value is expected before " + Shown(at) + ".",
                SpanOfToken(at));
            return InsertMissing(at);
        }

        Report(SyntaxCodes.UnexpectedToken, "Unexpected " + Shown(at) + ".", SpanOfToken(at));
        return SkipToResynchronisation(at);
    }

    private bool ExplainEnd(List<int> open, int top, Tk previous, bool afterSkip)
    {
        if (tokens.Count == 0)
        {
            // Everything that was there has been reported and repaired away.
            return false;
        }

        if (previous is not (Tk.LeftParen or Tk.LeftBracket or Tk.Comma) && TokenRules.NeedsOperandAfter(previous))
        {
            // `a === b`: the repair removed everything after `==`, so the condition only seems to end there; the `=` was reported.
            _silent |= afterSkip;
            Report(
                SyntaxCodes.UnexpectedEnd,
                "The condition ends after " + Shown(tokens.Count - 1) + "; " + (previous == Tk.Dot ? "a name" : "a value") + " is expected.",
                EndSpan());
            return InsertMissing(tokens.Count);
        }

        if (open.Count > 0)
        {
            var wanted = KindAt(top) == Tk.LeftParen ? Tk.RightParen : Tk.RightBracket;
            Report(
                SyntaxCodes.UnclosedBracket,
                Shown(top) + " is never closed; add `" + (wanted == Tk.RightParen ? ")" : "]") + "`.",
                SpanOfToken(top),
                wanted == Tk.RightParen ? ")" : "]");
            tokens.Add(new Token<Tk>(wanted, Synthetic(tokens.Count)));
            return true;
        }

        Report(SyntaxCodes.UnexpectedEnd, "The condition ends before it is complete.", EndSpan());
        return false;
    }

    /// <summary>A failure inside <c>tf(…)</c>: reports what the qualifier is missing and replaces the whole of it by a missing value.</summary>
    private bool TryQualifier(int at)
    {
        for (var stage = 2; stage <= 5; stage++)
        {
            var start = at - stage;
            if (start < 0 || KindAt(start) != Tk.Identifier || tokens[start].ToStringValue() != "tf" || KindAt(start + 1) != Tk.LeftParen)
            {
                continue;
            }

            var expected = new[] { Tk.String, Tk.RightParen, Tk.Dot };
            var matches = true;
            for (var i = 0; i < stage - 2 && matches; i++)
            {
                matches = KindAt(start + 2 + i) == expected[i] || (i == 0 && KindAt(start + 2) == Tk.Missing);
            }

            if (!matches)
            {
                continue;
            }

            var found = Shown(at);
            var frame = stage >= 3 ? tokens[start + 2].ToStringValue() : "\"1h\"";
            var written = "tf(" + frame + ")";
            switch (stage)
            {
                case 2:
                    Report(SyntaxCodes.BadTimeframeQualifier, "`tf(` must be followed by a timeframe in double quotes, like tf(\"1h\").ema(20); found " + found + ".", HereSpan(at));
                    break;
                case 3:
                    Report(SyntaxCodes.BadTimeframeQualifier, "The timeframe " + frame + " must be followed by `)`; found " + found + ".", HereSpan(at));
                    break;
                case 4:
                    Report(
                        SyntaxCodes.BadTimeframeQualifier,
                        written + " must be followed by `.` and the name of a feature, like tf(\"1h\").ema(20).",
                        SpanOfToken(start).To(SpanOfToken(start + 3)));
                    break;
                default:
                    Report(SyntaxCodes.BadTimeframeQualifier, "The name of a feature is expected after " + written + ".; found " + found + ".", HereSpan(at));
                    break;
            }

            var end = stage switch
            {
                4 => at,
                5 => at < tokens.Count && KindAt(at) is Tk.Number or Tk.String or Tk.True or Tk.False ? at + 1 : at,
                _ => EndOfQualifiedName(MatchingClose(start + 1) + 1),
            };
            return ReplaceWithMissing(start, Math.Max(end, start + 1));
        }

        return false;
    }

    /// <summary>After a broken <c>tf(…)</c>: the end of the <c>.name</c> and the arguments that follow it, if they are there, so that they are replaced with it and not reported again.</summary>
    private int EndOfQualifiedName(int afterQualifier)
    {
        if (KindAt(afterQualifier) != Tk.Dot || KindAt(afterQualifier + 1) is not (Tk.Identifier or Tk.Missing))
        {
            return afterQualifier;
        }

        var end = afterQualifier + 2;
        return KindAt(end) == Tk.LeftParen ? MatchingClose(end) + 1 : end;
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="openIndex"/>, or the token count when there is none.</summary>
    private int MatchingClose(int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < tokens.Count; i++)
        {
            var kind = tokens[i].Kind;
            if (kind is Tk.LeftParen or Tk.LeftBracket)
            {
                depth++;
            }
            else if (kind is Tk.RightParen or Tk.RightBracket)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return tokens.Count;
    }

    private TextSpan Synthetic(int index)
    {
        var position = index >= tokens.Count ? EndSpan() : SpanOfToken(index);
        return new TextSpan(source, new Position(position.Start, position.Line, position.Column), 0);
    }

    /// <summary>Puts a missing value before the token at <paramref name="index"/>. A second insertion at the same place deletes that token instead, so a fault that the stand-in does not cure cannot loop.</summary>
    private bool InsertMissing(int index)
    {
        _insertions[index] = _insertions.GetValueOrDefault(index) + 1;
        if (_insertions[index] > 1)
        {
            if (index >= tokens.Count)
            {
                return false;
            }

            tokens.RemoveAt(index);
            return true;
        }

        tokens.Insert(index, new Token<Tk>(Tk.Missing, Synthetic(index)));
        return true;
    }

    private bool ReplaceWithMissing(int from, int to)
    {
        to = Math.Min(Math.Max(to, from + 1), tokens.Count);
        var span = tokens[from].Span;
        tokens.RemoveRange(from, to - from);
        tokens.Insert(from, new Token<Tk>(Tk.Missing, span));
        return true;
    }

    /// <summary>Deletes the bracket at <paramref name="openIndex"/> with everything up to its closing bracket (or to the end if it is never closed).</summary>
    private void DeleteGroup(int openIndex)
    {
        var close = MatchingClose(openIndex);
        tokens.RemoveRange(openIndex, Math.Min(close + 1, tokens.Count) - openIndex);
    }

    /// <summary>Deletes the token at <paramref name="at"/> and all that follows up to the next safe place to resume: an <c>and</c>, <c>or</c> or comma at the same bracket level, or the closing bracket of the level.</summary>
    private bool SkipToResynchronisation(int at)
    {
        var depth = 0;
        var end = at;
        for (; end < tokens.Count; end++)
        {
            var kind = tokens[end].Kind;
            if (kind is Tk.LeftParen or Tk.LeftBracket)
            {
                depth++;
            }
            else if (kind is Tk.RightParen or Tk.RightBracket)
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
            }
            else if (depth == 0 && end > at && kind is Tk.And or Tk.Or or Tk.Comma)
            {
                break;
            }
        }

        _skippedToEnd = end >= tokens.Count;
        tokens.RemoveRange(at, Math.Max(end, at + 1) - at);
        return true;
    }
}
