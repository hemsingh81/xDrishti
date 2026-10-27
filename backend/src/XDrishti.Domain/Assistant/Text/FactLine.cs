namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// One fact of a briefing (spec P9-06): a sentence the app built from an engine output. <paramref name="Scope"/> is the ticket symbol the sentence is about, or empty for a global fact. <paramref name="Label"/> is the short name a text must keep the state of
/// (<c>regime</c>); <paramref name="State"/> is <see cref="Ok"/>, <see cref="Unavailable"/>, <see cref="Insufficient"/> or <see cref="NotEvaluable"/>; a fact in any state but ok may be mentioned in words only, never with a figure.
/// </summary>
public sealed record FactLine(string Scope, string Section, string Label, string Text, string State = FactLine.Ok)
{
    public const string Ok = "ok";

    public const string Unavailable = "unavailable";

    public const string Insufficient = "insufficient";

    public const string NotEvaluable = "not_evaluable";

    /// <summary>Whether a text may not state a figure for this fact.</summary>
    public bool NoFigures => State != Ok;
}
