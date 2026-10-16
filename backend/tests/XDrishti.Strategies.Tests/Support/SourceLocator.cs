namespace XDrishti.Strategies.Tests.Support;

/// <summary>Line and column of an offset, computed apart from the library: lines are numbered from 1 and end at <c>\n</c>, columns are numbered from 1 in UTF-16 code units.</summary>
internal static class SourceLocator
{
    public static (int Line, int Column) At(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }
}
