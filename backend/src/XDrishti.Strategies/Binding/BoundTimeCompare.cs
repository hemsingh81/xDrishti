using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// The keyword <c>time</c> compared with a <c>"HH:MM"</c> text, always with <c>time</c> on the left (<c>"09:45" &lt;= time</c> is turned into <c>time &gt;= "09:45"</c>). <see cref="MinuteOfDay"/> is the text in minutes after
/// midnight (<c>"09:45"</c> is 585). <c>time</c> is the India clock time at which the evaluated bar closed, the start of the last accepted 1-minute bar plus one minute, compared on whole minutes.
/// <see cref="KeywordSpan"/> is the keyword in the source (for messages about it). A time is never unknown once a bar has been accepted.
/// </summary>
internal sealed record BoundTimeCompare(BinaryOperator Operator, int MinuteOfDay, SourceSpan KeywordSpan, SourceSpan Span) : IBoundExpression
{
    public BoundType Type => BoundType.Bool;
}
