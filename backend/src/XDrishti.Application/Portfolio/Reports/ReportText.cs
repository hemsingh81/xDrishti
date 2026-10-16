using System.Text;
using System.Text.RegularExpressions;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Text of a report is untrusted: control characters become spaces, runs of spaces one space, and it is cut to a length. Also the lines every report starts with. Pure.</summary>
internal static partial class ReportText
{
    public const int MaxNameLength = 200;

    public const int MaxClientCodeLength = 32;

    public static string Clean(string? text, int max = MaxNameLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, max));
        var space = true;
        foreach (var c in text)
        {
            var blank = char.IsControl(c) || char.IsWhiteSpace(c);
            if (blank)
            {
                if (!space)
                {
                    builder.Append(' ');
                }

                space = true;
            }
            else
            {
                builder.Append(c);
                space = false;
            }

            if (builder.Length >= max)
            {
                break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The period of a report from its title line (<c>From 01-04-2026 to 15-10-2026</c>, <c>From 01-April-2026 to 15-October-2026</c>): the cells of the row are looked at together, nothing else is read from the row. Null for either date that is not there.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) Period(SheetRow? title)
    {
        if (title is null)
        {
            return (null, null);
        }

        var match = PeriodPattern().Match(string.Join(' ', title.Cells));
        if (!match.Success)
        {
            return (null, null);
        }

        DateOnly? from = ReportCells.TryParseDate(match.Groups["from"].Value, out var start) is null ? start : null;
        DateOnly? to = ReportCells.TryParseDate(match.Groups["to"].Value, out var end) is null ? end : null;
        return (from, to);
    }

    /// <summary>
    /// The client code (UCC) of a report, from the personal lines before its header — the only one of them that is read, and only to be compared (as a hash) with the code of the reports imported before: a row that starts with <c>UCC</c> or <c>Client code</c> (the code in the next
    /// cell, or after a colon in the same cell). Null when the report has none. Never logged, stored or returned.
    /// </summary>
    public static string? ClientCode(IEnumerable<SheetRow> beforeHeader)
    {
        ArgumentNullException.ThrowIfNull(beforeHeader);
        foreach (var row in beforeHeader)
        {
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var norm = ReportCells.Norm(row.Cells[i]);
                if (norm is "ucc" or "clientcode" or "clientid" or "ucccode")
                {
                    var next = row.Cells.Skip(i + 1).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                    if (next is not null)
                    {
                        return Code(next);
                    }

                    continue;
                }

                var inline = InlineCode().Match(row.Cells[i]);
                if (inline.Success)
                {
                    return Code(inline.Groups["code"].Value);
                }
            }
        }

        return null;
    }

    private static string? Code(string text)
    {
        var code = Clean(text, MaxClientCodeLength);
        return code.Length == 0 ? null : code;
    }

    [GeneratedRegex(@"From\s+(?<from>\d{1,2}[- ][A-Za-z0-9]+[- ]\d{4})\s+to\s+(?<to>\d{1,2}[- ][A-Za-z0-9]+[- ]\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^\s*(UCC|Client\s*(Code|ID))\s*[:\-]\s*(?<code>\S{1,32})\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex InlineCode();
}
