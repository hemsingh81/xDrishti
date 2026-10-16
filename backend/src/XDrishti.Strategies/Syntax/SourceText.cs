namespace XDrishti.Strategies.Syntax;

/// <summary>Lines and columns of positions in the condition text: lines are separated by <c>\n</c> (a <c>\r</c> before it is just a character at the end of the line), both are counted from 1, a column in UTF-16 code units.</summary>
internal static class SourceText
{
    /// <summary>The span from <paramref name="start"/> to <paramref name="end"/> (exclusive), with the line and column of its start.</summary>
    public static SourceSpan SpanOf(string source, int start, int end)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < start; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new SourceSpan(start, end, line, start - lineStart + 1);
    }
}
