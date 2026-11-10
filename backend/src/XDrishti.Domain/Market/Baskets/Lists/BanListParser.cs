using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>The parsed ban file: <see cref="Reason"/> is null when the file is valid; <see cref="TradeDate"/> is the date NSE names in the first line; an empty list is valid.</summary>
public sealed record BanFile(string? Reason, DateOnly? TradeDate, IReadOnlyList<string> Symbols, int Duplicates)
{
    public bool Ok => Reason is null;

    public static BanFile Rejected(string reason) => new(reason, null, [], 0);
}

/// <summary>
/// Reads <c>fo_secban.csv</c>: a first line <c>Securities in Ban For Trade Date 26-OCT-2026:</c> then <c>1,AMBUJACEM</c> rows. Tolerates a BOM, CRLF, blank lines and a repeated symbol; a first line that is not that header
/// (a 404 page, another file) or has no parseable date is rejected. A file with the first line only is valid (an empty ban list). No I/O.
/// </summary>
public static partial class BanListParser
{
    public const string BadHeader = "bad_header";
    public const string BadDate = "bad_date";
    public const string BadRow = "bad_row";

    public static BanFile Parse(string? text)
    {
        var lines = (text ?? string.Empty).TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("Securities in Ban For Trade Date", StringComparison.OrdinalIgnoreCase))
        {
            return BanFile.Rejected(BadHeader);
        }

        var match = DatePattern().Match(lines[0]);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, ["dd-MMM-yyyy", "d-MMM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return BanFile.Rejected(BadDate);
        }

        var symbols = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            var symbol = cells.Length >= 2 ? cells[1].Trim().ToUpperInvariant() : string.Empty;
            if (!SymbolPattern().IsMatch(symbol))
            {
                return BanFile.Rejected(BadRow);
            }

            if (seen.Add(symbol))
            {
                symbols.Add(symbol);
            }
            else
            {
                duplicates++;
            }
        }

        return new BanFile(null, date, symbols, duplicates);
    }

    [GeneratedRegex(@"Trade Date\s+(\d{1,2}-[A-Za-z]{3}-\d{4})", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^[A-Z0-9&\-]{1,20}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SymbolPattern();
}
