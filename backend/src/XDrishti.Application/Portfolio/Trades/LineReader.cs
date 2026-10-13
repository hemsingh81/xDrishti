using System.Text;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Splits comma separated text into records, reading one line at a time (so nothing but the current record is held). Fields in double quotes may contain commas, doubled quotes and line breaks; a quote in the middle of an
/// unquoted field is an ordinary character. Line numbers count physical lines from 1. Pure apart from reading the reader it is given.
/// </summary>
internal sealed class LineReader(TextReader reader)
{
    private int _line;

    /// <summary>The next record, or null at the end of the text.</summary>
    public async ValueTask<CsvRecord?> NextAsync(CancellationToken cancellationToken)
    {
        var first = await reader.ReadLineAsync(cancellationToken);
        if (first is null)
        {
            return null;
        }

        var start = ++_line;
        var fields = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var length = 0;
        var text = first;
        while (true)
        {
            length += text.Length + 1;
            Scan(text, fields, cell, ref inQuotes);
            if (!inQuotes)
            {
                break;
            }

            cell.Append('\n');
            var next = await reader.ReadLineAsync(cancellationToken);
            if (next is null)
            {
                break;
            }

            _line++;
            text = next;
        }

        fields.Add(cell.ToString());
        return new CsvRecord(start, fields, length);
    }

    private static void Scan(string text, List<string> fields, StringBuilder cell, ref bool inQuotes)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (c == '"' && cell.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }
    }
}
