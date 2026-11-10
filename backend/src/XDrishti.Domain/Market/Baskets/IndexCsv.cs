using System.Text;

namespace XDrishti.Domain.Market.Baskets;

/// <summary>One data row of an NSE index list.</summary>
public sealed record IndexRow(string Symbol, string Isin, string Name);

/// <summary>The parsed file: whether the header is the expected one, the rows (first of a repeated symbol only) and how many repeated symbol rows were left out.</summary>
public sealed record IndexFile(bool HeaderOk, IReadOnlyList<IndexRow> Rows, int DuplicateRows);

/// <summary>Reads the NSE index constituent CSV: <c>Company Name,Industry,Symbol,Series,ISIN Code</c>. Tolerates a BOM, CRLF and blank lines; no I/O.</summary>
public static class IndexCsv
{
    public const string Header = "Company Name,Industry,Symbol,Series,ISIN Code";

    public static IndexFile Parse(string? text)
    {
        var lines = (text ?? string.Empty).TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0 || !string.Equals(string.Join(',', Split(lines[0]).Select(c => c.Trim())), Header, StringComparison.OrdinalIgnoreCase))
        {
            return new IndexFile(false, [], 0);
        }

        var rows = new List<IndexRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        foreach (var line in lines.Skip(1))
        {
            var cells = Split(line);
            if (cells.Count < 5)
            {
                continue;
            }

            var symbol = cells[2].Trim().ToUpperInvariant();
            if (symbol.Length == 0)
            {
                continue;
            }

            if (!seen.Add(symbol))
            {
                duplicates++;
                continue;
            }

            rows.Add(new IndexRow(symbol, cells[4].Trim().ToUpperInvariant(), cells[0].Trim()));
        }

        return new IndexFile(true, rows, duplicates);
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
