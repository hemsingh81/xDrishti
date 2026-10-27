namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>A figure of the answer that the tool results do not hold: its place in the text (UTF-16 code units, <paramref name="End"/> exclusive) and the text itself.</summary>
public sealed record UngroundedSpan(int Start, int End, string Text);
