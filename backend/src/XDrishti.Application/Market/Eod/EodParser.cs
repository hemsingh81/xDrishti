using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Import;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary>
/// Reads NSE's daily files into logical rows (CF-04 Part A). Fail closed: a header that is not in <see cref="EodFormats"/> refuses the whole file (<c>history.unknown_format</c>) and a row dated other than the requested
/// day refuses it too (<c>history.date_mismatch</c>); the caller writes nothing then. A row with an unreadable number is counted and skipped. Pure: no I/O, no clock.
/// </summary>
internal static class EodParser
{
    public const decimal PercentTolerance = 0.01m;

    /// <summary>A bhavcopy whose unreadable rows are more than this share of the rows of the wanted series is refused whole (<c>history.unknown_format</c>): a column that moved reads as "every number unreadable", not as a few bad rows.</summary>
    public const decimal MaxRejectedShare = 0.05m;

    private static readonly string[] DateFormats = ["dd-MMM-yyyy", "d-MMM-yyyy", "yyyy-MM-dd", "ddMMyyyy"];

    public static async ValueTask<Result<BhavParse>> ParseBhavcopyAsync(byte[] content, DateOnly expected, IReadOnlyCollection<string> series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(series);
        using var text = new StringReader(Decode(content));
        var reader = new CsvRecordReader(text);
        var headerLine = await NextAsync(reader, cancellationToken);
        if (headerLine is null)
        {
            return Result.Failure<BhavParse>(EodErrors.UnknownFormat("bhavcopy", "empty file"));
        }

        var header = (await reader.SplitAsync(headerLine, cancellationToken)).Select(h => h.Trim()).ToArray();
        var layout = EodFormats.Layouts.FirstOrDefault(l => Missing(l, header).Count == 0);
        if (layout is null)
        {
            var closest = EodFormats.Layouts.MinBy(l => Missing(l, header).Count)!;
            return Result.Failure<BhavParse>(EodErrors.UnknownFormat("bhavcopy", "missing " + string.Join(", ", Missing(closest, header).Take(5))));
        }

        var at = layout.Columns.ToDictionary(c => c.Key, c => Array.FindIndex(header, h => string.Equals(h, c.Value, StringComparison.OrdinalIgnoreCase)));
        var wanted = new HashSet<string>(series, StringComparer.OrdinalIgnoreCase);
        var rows = new List<EodRow>();
        var other = 0;
        var rejected = 0;
        var seen = new HashSet<(string, string)>();
        while (await NextAsync(reader, cancellationToken) is { } line)
        {
            var f = await reader.SplitAsync(line, cancellationToken);
            string? Cell(EodField field) => at[field] is >= 0 and var i && i < f.Length ? f[i].Trim() : null;

            var date = ReadDate(Cell(EodField.Date));
            if (date is not { } day || day != expected)
            {
                return Result.Failure<BhavParse>(EodErrors.DateMismatch("bhavcopy", expected));
            }

            var serie = Cell(EodField.Series);
            var symbol = Cell(EodField.Symbol);
            if (string.IsNullOrEmpty(serie) || string.IsNullOrEmpty(symbol))
            {
                rejected++;
                continue;
            }

            if (!wanted.Contains(serie))
            {
                other++;
                continue;
            }

            var row = ReadRow(day, symbol, serie, Cell);
            if (row is null)
            {
                rejected++;
            }
            else if (!seen.Add((row.Symbol, row.Series)))
            {
                return Result.Failure<BhavParse>(EodErrors.DuplicateRow("bhavcopy", row.Symbol, row.Series));
            }
            else
            {
                rows.Add(row);
            }
        }

        if (rejected > 0 && (rows.Count == 0 || (decimal)rejected / (rows.Count + rejected) > MaxRejectedShare))
        {
            return Result.Failure<BhavParse>(EodErrors.UnknownFormat("bhavcopy", $"{rejected} of {rows.Count + rejected} rows could not be read"));
        }

        return Result.Success(new BhavParse(rows, other, rejected, layout.Name));
    }

    public static async ValueTask<Result<DeliveryParse>> ParseDeliveryAsync(byte[] content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var text = new StringReader(Decode(content));
        var reader = new CsvRecordReader(text);
        var h = EodFormats.DeliveryHeader;
        int[]? at = null;
        var figures = new Dictionary<(string, string), DeliveryFigures>();
        var rejected = 0;
        while (await NextAsync(reader, cancellationToken) is { } line)
        {
            var f = (await reader.SplitAsync(line, cancellationToken)).Select(c => c.Trim()).ToArray();
            if (at is null)
            {
                if (f.Length > 0 && f[0].StartsWith(h.Record, StringComparison.OrdinalIgnoreCase))
                {
                    at = [Find(f, h.Symbol), Find(f, h.Series), Find(f, h.Traded), Find(f, h.Delivered), Find(f, h.Percent)];
                    if (at.Any(i => i < 0))
                    {
                        return Result.Failure<DeliveryParse>(EodErrors.UnknownFormat("delivery", "a column is missing"));
                    }
                }

                continue; // preamble lines before the header
            }

            // Data lines start with record type 20; anything else after the header (a footer) is ignored.
            if (f.Length <= at.Max() || f[0] != "20")
            {
                continue;
            }

            if (!long.TryParse(f[at[3]], NumberStyles.None, CultureInfo.InvariantCulture, out var delivered) || f[at[0]].Length == 0)
            {
                rejected++;
                continue;
            }

            decimal? percent = decimal.TryParse(f[at[4]], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) ? p : null;
            figures[(f[at[0]], f[at[1]])] = new DeliveryFigures(delivered, percent);
        }

        return at is null
            ? Result.Failure<DeliveryParse>(EodErrors.UnknownFormat("delivery", "no header line"))
            : Result.Success(new DeliveryParse(figures, rejected));
    }

    /// <summary>Puts delivery on the price rows. A delivery above the traded quantity is rejected (the price row stays, without delivery); the percent is recomputed and must agree with the file's own figure within 0.01 pp.</summary>
    public static DeliveryMerge Merge(IReadOnlyList<EodRow> rows, DeliveryParse delivery)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(delivery);
        var merged = 0;
        var rejected = 0;
        var result = new List<EodRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!delivery.Figures.TryGetValue((row.Symbol, row.Series), out var d))
            {
                result.Add(row);
                continue;
            }

            var percent = Percent(d.Quantity, row.TradedQty);
            if (percent is null && d.Quantity == 0)
            {
                result.Add(row); // nothing traded: both delivery fields stay null (the table needs them both set or both null)
                continue;
            }

            if (d.Quantity > row.TradedQty || (percent is { } computed && d.FilePercent is { } own && Math.Abs(computed - own) > PercentTolerance))
            {
                rejected++;
                result.Add(row);
                continue;
            }

            merged++;
            result.Add(row with { DeliveryQty = d.Quantity, DeliveryPct = percent });
        }

        return new DeliveryMerge(result, merged, rejected);
    }

    /// <summary>Delivery quantity as a percent of traded quantity, two decimals (null when nothing traded).</summary>
    public static decimal? Percent(long delivery, long traded) => traded <= 0 ? null : Math.Round(delivery * 100m / traded, 2, MidpointRounding.AwayFromZero);

    public static DateOnly? ReadDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static EodRow? ReadRow(DateOnly day, string symbol, string series, Func<EodField, string?> cell)
    {
        if (!Money(cell(EodField.Open), out var open) || !Money(cell(EodField.High), out var high) || !Money(cell(EodField.Low), out var low) || !Money(cell(EodField.Close), out var close)
            || !long.TryParse(cell(EodField.TradedQty), NumberStyles.None, CultureInfo.InvariantCulture, out var traded)
            || !decimal.TryParse(cell(EodField.Turnover), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var turnover) || turnover < 0)
        {
            return null;
        }

        decimal? prev = Money(cell(EodField.PrevClose), out var p) ? p : null;
        long? trades = long.TryParse(cell(EodField.Trades), NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : null;
        var isin = cell(EodField.Isin);
        return new EodRow(day, series, symbol, string.IsNullOrEmpty(isin) ? null : isin, open, high, low, close, prev, traded, turnover, trades, null, null);
    }

    private static bool Money(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static List<string> Missing(EodLayout layout, string[] header) =>
        [.. layout.Columns.Where(c => !layout.Optional.Contains(c.Key) && !header.Contains(c.Value, StringComparer.OrdinalIgnoreCase)).Select(c => c.Value)];

    private static int Find(string[] header, string prefix) => Array.FindIndex(header, c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string Decode(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static async ValueTask<string?> NextAsync(CsvRecordReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }
}
