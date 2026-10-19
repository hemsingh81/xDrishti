using System.Text;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// A small reader of RFC 4180 text for the check mode (spec P3-09, V8): UTF-8 text already decoded by the host, a leading byte order mark dropped, <c>\n</c>, <c>\r\n</c> and a lone <c>\r</c> as line ends, quotes doubled inside a quoted cell. It
/// refuses what is not CSV (an unterminated quote, a quote in the middle of an unquoted cell, a cell longer than the cap, more rows than the cap) and says so; it never throws on text. Cells are not trimmed.
/// </summary>
internal static class CsvText
{
    public const int MaxCellChars = 2000;

    public const int MaxRows = 100_000;

    /// <summary>Reads the rows, or returns the reason the text is not CSV.</summary>
    public static (List<List<string>> Rows, string? Problem) Read(string text)
    {
        var rows = new List<List<string>>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        var quoted = false;
        var inQuotes = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"' && cell.Length == 0 && !quoted)
            {
                inQuotes = true;
                quoted = true;
            }
            else if (c == '"')
            {
                return (rows, "a quote in the middle of a cell");
            }
            else if (c == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                quoted = false;
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                cells.Add(cell.ToString());
                cell.Clear();
                quoted = false;
                rows.Add(cells);
                cells = [];
                if (rows.Count > MaxRows)
                {
                    return (rows, "more than " + MaxRows.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rows");
                }
            }
            else
            {
                cell.Append(c);
            }

            if (cell.Length > MaxCellChars)
            {
                return (rows, "a cell longer than " + MaxCellChars.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters");
            }
        }

        if (inQuotes)
        {
            return (rows, "a quote that is never closed");
        }

        if (cell.Length > 0 || cells.Count > 0 || quoted)
        {
            cells.Add(cell.ToString());
            rows.Add(cells);
        }

        return (rows, null);
    }
}
