using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// What one compile of one condition shares between the binder and the lint: the source text (to quote it in messages), the strategy's timeframe, the parameter values (read, never changed), the problems found so far
/// and two small memories (the specs the catalog was already asked about, the parameters already warned about). It lives for one call of the compiler, so nothing is shared between compiles or threads.
/// </summary>
internal sealed class BindingScope(string source, Timeframe evaluationTimeframe, IReadOnlyDictionary<string, double> parameters)
{
    private const int MaxQuoted = 60;

    private readonly List<CatalogAnswerEntry> _catalogAnswers = [];

    private readonly HashSet<string> _warnedShadows = new(StringComparer.Ordinal);

    /// <summary>The text of the condition.</summary>
    public string Source { get; } = source;

    /// <summary>The timeframe of the strategy: where an unqualified feature is read and what <c>time</c> means.</summary>
    public Timeframe EvaluationTimeframe { get; } = evaluationTimeframe;

    /// <summary>The strategy parameters, name to number.</summary>
    public IReadOnlyDictionary<string, double> Parameters { get; } = parameters;

    /// <summary>Every problem found so far, in the order it was found (the compiler sorts them by position at the end).</summary>
    public List<Problem> Problems { get; } = [];

    public void Error(string code, string message, SourceSpan span, string? suggestion = null) =>
        Problems.Add(new Problem(code, message, span, suggestion));

    public void Warning(string code, string message, SourceSpan span, string? suggestion = null) =>
        Problems.Add(new Problem(code, message, span, suggestion, ProblemSeverity.Warning));

    public void Information(string code, string message, SourceSpan span) =>
        Problems.Add(new Problem(code, message, span, null, ProblemSeverity.Information));

    /// <summary>The text of the span as written.</summary>
    public string Text(SourceSpan span) => Source.Substring(span.Start, span.Length);

    /// <summary>The text of the span between backticks, shortened when it is long, for a message.</summary>
    public string Quoted(SourceSpan span) => Quote(Text(span));

    /// <summary>Text between backticks, shortened when it is long.</summary>
    public static string Quote(string text) => "`" + (text.Length > MaxQuoted ? text[..(MaxQuoted - 3)] + "..." : text) + "`";

    /// <summary>True the first time it is asked for a parameter name, so that a parameter that hides a name is warned about once.</summary>
    public bool FirstShadowOf(string parameter) => _warnedShadows.Add(parameter);

    /// <summary>
    /// What the catalog says about the spec: the field errors the frame's own validation gives for a frame of this one feature (a parameter outside its range, a combination such as <c>fast</c> not below <c>slow</c>, a
    /// timeframe the feature cannot run on), without the ones about the frame's options (a context or expiry dates are the caller's to give). Empty when the spec is valid. Asked once per spec.
    /// </summary>
    public IReadOnlyList<FieldError> CatalogAnswer(FeatureSpec spec)
    {
        foreach (var entry in _catalogAnswers)
        {
            if (entry.Spec.Equals(spec))
            {
                return entry.Answer;
            }
        }

        var created = FeatureFrame.Create([spec]);
        IReadOnlyList<FieldError> answer = created.IsSuccess
            ? []
            : [.. (created.Error.Fields ?? []).Where(f => !f.Field.StartsWith("options.", StringComparison.Ordinal))];
        _catalogAnswers.Add(new CatalogAnswerEntry(spec, answer));
        return answer;
    }

    /// <summary>What the catalog said about one spec. A class, and a list searched in order: a condition has a handful of specs, and a generic collection keyed by a struct costs more to start up than the search costs to run.</summary>
    private sealed record CatalogAnswerEntry(FeatureSpec Spec, IReadOnlyList<FieldError> Answer);
}
