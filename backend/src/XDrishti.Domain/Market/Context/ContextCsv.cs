using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Market.Context;

/// <summary>
/// Pure: reads the text of an owner-dropped NSE daily CSV (P3-01d, fallback D). NIFTY 50 (niftyindices.com: <c>Date, Open, High, Low, Close</c>, optionally an <c>Index Name</c> first) and INDIA VIX (nseindia.com: <c>Date, Open, High, Low, Close, Prev. Close, Change, % Change</c>).
/// Tolerant of a BOM, header case and spacing, quoted numbers with thousands commas, trailing spaces and the date forms <c>dd-MMM-yyyy</c>, <c>dd MMM yyyy</c>, <c>dd-MM-yyyy</c>, <c>dd/MM/yyyy</c>, <c>yyyy-MM-dd</c>. A row that cannot be read is refused with a closed code, never guessed.
/// </summary>
public static partial class ContextCsv
{
    private const double MaxVix = 200;

    private static readonly string[] DateFormats =
    [
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd",
    ];

    /// <summary>Reads <paramref name="text"/>; <paramref name="fileName"/> (may be null) is only a hint for the kind when the header does not say.</summary>
    public static ContextCsvParsed Parse(string text, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.TrimStart('﻿').Split('\n');
        var headerAt = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerAt < 0)
        {
            return Failed(ContextCsvKind.Index, ContextCsvCodes.Empty);
        }

        var header = Split(lines[headerAt]).Select(Normalise).ToList();
        var date = header.IndexOf("date");
        var high = header.IndexOf("high");
        var low = header.IndexOf("low");
        var close = header.IndexOf("close");
        if (date < 0 || close < 0)
        {
            return Failed(ContextCsvKind.Index, ContextCsvCodes.NoHeader);
        }

        var indexName = header.IndexOf("indexname");
        var nameSaysVix = fileName?.Contains("vix", StringComparison.OrdinalIgnoreCase) ?? false;
        var vixShaped = header.Contains("prevclose") || header.Contains("change") || nameSaysVix;
        var kind = vixShaped ? ContextCsvKind.Vix : ContextCsvKind.Index;

        // A VIX-shaped file (an equity export has the same columns) is taken only when its Index Name column (checked row by row) or its file name says VIX.
        if (kind == ContextCsvKind.Vix && indexName < 0 && !nameSaysVix)
        {
            return Failed(kind, ContextCsvCodes.UnknownIndex);
        }
        if (kind == ContextCsvKind.Index && (high < 0 || low < 0))
        {
            return Failed(kind, ContextCsvCodes.MissingColumn);
        }

        // A file with no Index Name column must be named for NIFTY 50 itself (not NIFTY 500, NIFTY 50 Equal Weight or NIFTY50 Value 20): the table cannot be corrected once written, so another index is never taken on trust.
        if (kind == ContextCsvKind.Index && indexName < 0 && !(fileName is not null && Nifty50Name().IsMatch(fileName)))
        {
            return Failed(kind, ContextCsvCodes.UnknownIndex);
        }

        var widest = Math.Max(Math.Max(date, close), Math.Max(high, low));
        var rows = new List<ContextCsvRow>();
        var rejects = new List<ContextCsvReject>();
        var seen = new HashSet<DateOnly>();
        var numericBadDate = false;
        for (var i = headerAt + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var line = i + 1;
            var cells = Split(lines[i]);
            if (cells.Count <= widest)
            {
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.ShortRow, string.Empty));
                continue;
            }

            if (indexName >= 0 && !NameMatches(cells[indexName], kind))
            {
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.WrongIndex, "index_name"));
                continue;
            }

            if (!DateOnly.TryParseExact(cells[date].Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var day))
            {
                numericBadDate |= NumericDate().IsMatch(cells[date].Trim());
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.BadDate, "date"));
                continue;
            }

            var closeValue = Number(cells[close]);
            var highValue = high >= 0 ? Number(cells[high]) : null;
            var lowValue = low >= 0 ? Number(cells[low]) : null;
            if (closeValue is null || (kind == ContextCsvKind.Index && (highValue is null || lowValue is null)))
            {
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.BadNumber, closeValue is null ? "close" : "high_low"));
                continue;
            }

            if (!Plausible(kind, highValue, lowValue, closeValue.Value))
            {
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.BadRange, "prices"));
                continue;
            }

            if (!seen.Add(day))
            {
                rejects.Add(new ContextCsvReject(line, ContextCsvCodes.DuplicateDate, "date"));
                continue;
            }

            rows.Add(new ContextCsvRow(day, highValue, lowValue, closeValue.Value, line));
        }

        // Good rows next to unreadable numeric dates is the sign of a day/month swap: the whole file is refused, never half-loaded.
        if (numericBadDate && rows.Count > 0)
        {
            return Failed(kind, ContextCsvCodes.MixedDates);
        }

        return new ContextCsvParsed(kind, rows, rejects, rows.Count == 0 && rejects.Count == 0 ? ContextCsvCodes.Empty : null);
    }

    [GeneratedRegex(@"^nifty[ _-]?50(?=[_.\-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Nifty50Name();

    [GeneratedRegex(@"^\d{1,4}[-/]\d{1,2}[-/]\d{1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDate();

    private static ContextCsvParsed Failed(ContextCsvKind kind, string code) => new(kind, [], [], code);

    private static bool NameMatches(string name, ContextCsvKind kind)
    {
        var n = name.Trim();
        return kind == ContextCsvKind.Vix
            ? n.Contains("VIX", StringComparison.OrdinalIgnoreCase)
            : string.Equals(n, "NIFTY 50", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Plausible(ContextCsvKind kind, double? high, double? low, double close)
    {
        if (kind == ContextCsvKind.Vix)
        {
            return close > 0 && close <= MaxVix && (high is null || low is null || (low > 0 && high >= low && close >= low && close <= high));
        }

        return low > 0 && high >= low && close >= low && close <= high;
    }

    private static double? Number(string cell)
    {
        var s = cell.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    private static string Normalise(string name)
    {
        var b = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetter(c) || c == '%')
            {
                b.Append(char.ToLowerInvariant(c));
            }
        }

        return b.ToString();
    }

    /// <summary>Comma split that keeps a quoted cell (with commas and doubled quotes) whole.</summary>
    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var text = line.TrimEnd('\r', '\n');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
