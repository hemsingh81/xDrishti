using System.Text;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>
/// A small reader of RFC 4180 text written for the tests and independent of the writer: rows end in a line feed (a carriage return before it is kept as part of the cell, as the writer never produces one), a cell that starts with a quote
/// ends at the quote that is not doubled, and nothing but a comma or a line end may follow it. It throws on text a strict reader would refuse, so a writer that produces such text fails.
/// </summary>
internal static class Rfc4180Reader
{
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var i = 0;
        var atCellStart = true;
        while (i < text.Length)
        {
            var c = text[i];
            if (atCellStart && c == '"')
            {
                i++;
                while (true)
                {
                    if (i >= text.Length)
                    {
                        throw new FormatException("A quoted cell is not closed.");
                    }

                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    cell.Append(text[i++]);
                }

                if (i < text.Length && text[i] is not (',' or '\n'))
                {
                    throw new FormatException("Text follows the closing quote of a cell at " + i + ".");
                }

                atCellStart = false;
                continue;
            }

            if (c == '"')
            {
                throw new FormatException("A quote inside an unquoted cell at " + i + ".");
            }

            if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                atCellStart = true;
                i++;
                continue;
            }

            if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
                atCellStart = true;
                i++;
                continue;
            }

            cell.Append(c);
            atCellStart = false;
            i++;
        }

        if (cell.Length > 0 || row.Count > 0 || !atCellStart)
        {
            throw new FormatException("The text does not end with a line feed.");
        }

        return rows;
    }
}
