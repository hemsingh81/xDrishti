namespace XDrishti.Strategies;

/// <summary>A stretch of the condition text: where it starts and ends (zero-based UTF-16 offsets, <see cref="End"/> exclusive) and where it starts in lines and columns (both one-based).</summary>
public readonly record struct SourceSpan(int Start, int End, int Line, int Column)
{
    /// <summary>The number of UTF-16 code units the span covers; 0 for a position between two characters (for example the end of the text).</summary>
    public int Length => End - Start;

    /// <summary>The span from the start of this one to the end of <paramref name="last"/>: the text of a node made of two parts.</summary>
    public SourceSpan To(SourceSpan last) => new(Start, last.End, Line, Column);
}
