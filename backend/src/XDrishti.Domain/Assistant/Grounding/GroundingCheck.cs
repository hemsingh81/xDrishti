namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>
/// The grounding check (spec P9-03 AC 6, P9-00 citation rule): every figure of an answer must be a number of the tool results of the turn (or of the owner's question). A figure matches a source number when the values are equal after normalisation, or equal within half a unit
/// of the figure's own last printed digit, or equal after ×100 (a percent against a ratio), ignoring the sign (a loss of 0.3 matches -0.3); a count printed as a plain integer must be exact, or match a source with decimals after rounding. A difference the model computed itself is not in
/// any source, so it is flagged: the only way to pass a derived number is to take it from the tool <c>calc</c>. The check reads digits only; it narrows the problem, it does not close it. Pure; never changes the text.
/// </summary>
public static class GroundingCheck
{
    /// <summary>Checks <paramref name="answer"/> against the question and the texts of every tool result (never the arguments of the model's own tool calls: a figure it invented and echoed there would pass) of the turn.</summary>
    public static GroundingResult Check(string answer, string question, IEnumerable<string> toolResults)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(toolResults);
        var figures = NumberTokenizer.Figures(answer);
        if (figures.Count == 0)
        {
            return new GroundingResult(GroundingStatus.NoNumbers, 0, []);
        }

        var sources = new List<ParsedNumber>();
        sources.AddRange(NumberTokenizer.Sources(question));
        foreach (var text in toolResults)
        {
            sources.AddRange(NumberTokenizer.Sources(text));
        }

        var distinct = sources.Select(s => s.Value).Distinct().ToArray();
        var unmatched = new List<UngroundedSpan>();
        foreach (var figure in figures)
        {
            if (figure.Number is not { } number || !Matches(number, distinct))
            {
                unmatched.Add(new UngroundedSpan(figure.Start, figure.End, figure.Text));
            }
        }

        return new GroundingResult(unmatched.Count == 0 ? GroundingStatus.Grounded : GroundingStatus.Ungrounded, figures.Count, unmatched);
    }

    private static bool Matches(ParsedNumber figure, decimal[] sources)
    {
        foreach (var source in sources)
        {
            if (Within(figure, source) || (figure.IsPercent && Within(figure, source * 100m)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Within(ParsedNumber figure, decimal source)
    {
        if (figure.IsPlainInteger)
        {
            // A count: exact against a whole source; a source with decimals may be rounded to the integer (money).
            return source == figure.Value || (source != decimal.Truncate(source) && Math.Abs(figure.Value - source) <= 0.5m);
        }

        return Math.Abs(figure.Value - source) <= figure.HalfUnit;
    }
}
