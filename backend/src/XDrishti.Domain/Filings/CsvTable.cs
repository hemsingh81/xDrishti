using System.Text;

namespace XDrishti.Domain.Filings;

/// <summary>
/// A small RFC 4180 reader for the owner's csv files (P15-04): comma separated, double-quote quoting with doubled quotes, LF or CRLF, a leading BOM accepted, empty lines skipped, a quoted field may hold a line break. A malformed record (a quote inside an unquoted field,
/// text after a closing quote, an unclosed quote) comes back with null fields and never stops the records after it, except an unclosed quote which swallows the rest of the text. Pure; it never throws on bad input.
/// </summary>
public static class CsvTable
{
    public static IReadOnlyList<CsvRecord> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var records = new List<CsvRecord>();
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        var line = 1;
        while (i < text.Length)
        {
            if (text[i] is '\n' or '\r')
            {
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                line++;
                continue;
            }

            var startLine = line;
            var fields = new List<string>();
            var field = new StringBuilder();
            var bad = false;
            var endOfRecord = false;
            var atFieldStart = true;
            var quoted = false;
            var afterQuote = false;
            while (i < text.Length && !endOfRecord)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        quoted = false;
                        afterQuote = true;
                        i++;
                        continue;
                    }

                    line += c == '\n' ? 1 : 0;
                    field.Append(c);
                    i++;
                    continue;
                }

                if (c == '\n' || c == '\r')
                {
                    i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    line++;
                    endOfRecord = true;
                    continue;
                }

                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    afterQuote = false;
                    i++;
                    continue;
                }

                if (c == '"' && atFieldStart)
                {
                    quoted = true;
                    atFieldStart = false;
                    i++;
                    continue;
                }

                if (c == '"' || afterQuote)
                {
                    bad = true;
                }

                atFieldStart = false;
                field.Append(c);
                i++;
            }

            if (quoted)
            {
                bad = true;
            }

            fields.Add(field.ToString());
            records.Add(new CsvRecord(startLine, bad ? null : fields));
        }

        return records;
    }
}
