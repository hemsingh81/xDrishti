using System.Globalization;

namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>One data row of <c>fo_mktlots.csv</c> with the lot of the nearest expiry column.</summary>
public sealed record LotRow(string Underlying, string Symbol, int LotSize, string ExpiryLabel);

/// <summary>The parsed lot file: <see cref="Reason"/> null when valid; rows without a lot in the nearest column are counted, not listed.</summary>
public sealed record LotFile(string? Reason, IReadOnlyList<LotRow> Rows, int RowsWithoutLot)
{
    public bool Ok => Reason is null;

    public static LotFile Rejected(string reason) => new(reason, [], 0);
}

/// <summary>
/// Reads <c>fo_mktlots.csv</c> (<c>UNDERLYING,SYMBOL,OCT-26,NOV-26,...</c>): every cell is trimmed; the lot of a row is the cell of the nearest expiry column, the first whose month-end is on or after the fetch date.
/// A wrong header, no usable expiry column, a non-numeric or non-positive lot in that column or an empty file is rejected. Which rows are shares or indices is decided by the caller (it knows the master). No I/O.
/// </summary>
public static class LotSizeParser
{
    public const string Empty = "empty";
    public const string BadHeader = "bad_header";
    public const string NoExpiry = "no_expiry";
    public const string BadLot = "bad_lot";

    public static LotFile Parse(string? text, DateOnly fetchDate)
    {
        var lines = (text ?? string.Empty).TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0)
        {
            return LotFile.Rejected(Empty);
        }

        var header = lines[0].Split(',').Select(c => c.Trim()).ToArray();
        if (header.Length < 3 || !header[0].Equals("UNDERLYING", StringComparison.OrdinalIgnoreCase) || !header[1].Equals("SYMBOL", StringComparison.OrdinalIgnoreCase))
        {
            return LotFile.Rejected(BadHeader);
        }

        var column = -1;
        for (var i = 2; i < header.Length; i++)
        {
            if (header[i].Length == 0)
            {
                continue; // the file ends its header with a comma
            }

            if (!TryMonthEnd(header[i], out var monthEnd))
            {
                return LotFile.Rejected(BadHeader);
            }

            if (column < 0 && monthEnd >= fetchDate)
            {
                column = i;
            }
        }

        if (column < 0)
        {
            return LotFile.Rejected(NoExpiry);
        }

        var rows = new List<LotRow>();
        var without = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',').Select(c => c.Trim()).ToArray();
            if (cells.Length < 2 || cells[1].Length == 0)
            {
                continue;
            }

            var cell = cells.Length > column ? cells[column] : string.Empty;
            if (cell.Length == 0 || cell == "-")
            {
                without++;
                continue;
            }

            if (!int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var lot) || lot <= 0)
            {
                return LotFile.Rejected(BadLot);
            }

            var symbol = cells[1].ToUpperInvariant();
            if (seen.Add(symbol))
            {
                rows.Add(new LotRow(cells[0], symbol, lot, header[column].ToUpperInvariant()));
            }
        }

        return new LotFile(null, rows, without);
    }

    private static bool TryMonthEnd(string label, out DateOnly monthEnd)
    {
        monthEnd = default;
        if (!DateTime.TryParseExact(label, "MMM-yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return false;
        }

        monthEnd = DateOnly.FromDateTime(month).AddMonths(1).AddDays(-1);
        return true;
    }
}
