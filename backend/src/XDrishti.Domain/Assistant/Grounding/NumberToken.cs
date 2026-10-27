namespace XDrishti.Domain.Assistant.Grounding;

/// <summary>A figure found in a text: where it is (UTF-16 code units, <paramref name="End"/> exclusive), its text, and its value (null when it does not fit a decimal: such a figure is never found in a source).</summary>
public sealed record NumberToken(int Start, int End, string Text, ParsedNumber? Number);
