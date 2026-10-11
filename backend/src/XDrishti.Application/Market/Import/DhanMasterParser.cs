using System.Globalization;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Import;

/// <summary>
/// Reads Dhan's <c>api-scrip-master-detailed.csv</c> as a stream (207k rows, 35 MB) and keeps only the rows in scope (ADR 0010).
/// Fails closed on structural problems only: an unreadable header, a missing column the importer reads, a line that is not a table row,
/// no in-scope rows at all, or too many unreadable rows (more than <see cref="MaxBadRows"/> or more than 1 % of the in-scope rows).
/// Anything smaller is tolerated and reported: extra or unused columns, and individual in-scope rows that are malformed or do not fit the
/// stored limits are skipped with a warning naming the Dhan id, and their instruments are protected from retirement.
/// </summary>
internal static class DhanMasterParser
{
    public const string FailureCode = "instruments.source_invalid";

    /// <summary>Progress is reported every this many rows.</summary>
    public const int ProgressEvery = 20_000;

    public const int MaxBadRows = 50;

    /// <summary>Row warnings listed one by one; the rest are counted.</summary>
    public const int MaxListedRowWarnings = 10;

    /// <summary>The columns of the file as published (2026-10-11), in order; the file also ends with an empty column.</summary>
    internal static readonly string[] Columns =
    [
        "EXCH_ID", "SEGMENT", "SECURITY_ID", "ISIN", "INSTRUMENT", "UNDERLYING_SECURITY_ID", "UNDERLYING_SYMBOL", "SYMBOL_NAME", "DISPLAY_NAME",
        "INSTRUMENT_TYPE", "SERIES", "LOT_SIZE", "SM_EXPIRY_DATE", "STRIKE_PRICE", "OPTION_TYPE", "TICK_SIZE", "EXPIRY_FLAG", "BRACKET_FLAG",
        "COVER_FLAG", "ASM_GSM_FLAG", "ASM_GSM_CATEGORY", "BUY_SELL_INDICATOR", "BUY_CO_MIN_MARGIN_PER", "BUY_CO_SL_RANGE_MAX_PERC",
        "BUY_CO_SL_RANGE_MIN_PERC", "BUY_BO_MIN_MARGIN_PER", "BUY_BO_PROFIT_RANGE_MAX_PERC", "BUY_BO_PROFIT_RANGE_MIN_PERC", "MTF_LEVERAGE",
        "SM_UPPER_LIMIT", "SM_LOWER_LIMIT", "SM_FREEZE_QTY",
    ];

    /// <summary>The columns the importer reads; without any of them the file cannot be understood.</summary>
    internal static readonly string[] Required =
    [
        "EXCH_ID", "SEGMENT", "SECURITY_ID", "ISIN", "INSTRUMENT", "UNDERLYING_SYMBOL", "SYMBOL_NAME", "DISPLAY_NAME", "INSTRUMENT_TYPE", "SERIES",
        "LOT_SIZE", "SM_EXPIRY_DATE", "TICK_SIZE", "ASM_GSM_FLAG", "ASM_GSM_CATEGORY", "MTF_LEVERAGE", "SM_FREEZE_QTY",
    ];

    public static async ValueTask<Result<MasterParse>> ParseAsync(
        Stream content, Func<long, ValueTask>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var text = new StreamReader(content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
        var csv = new CsvRecordReader(text);
        var warnings = new List<string>();

        string[]? headerFields;
        try
        {
            var headerLine = await csv.ReadLineAsync(cancellationToken);
            headerFields = headerLine is null ? null : await csv.SplitAsync(headerLine, cancellationToken);
        }
        catch (FormatException ex)
        {
            return Fail<MasterParse>("Dhan's file has an unreadable header: " + ex.Message);
        }

        var headerProblem = CheckHeader(headerFields, warnings);
        if (headerProblem is not null)
        {
            return Fail<MasterParse>(headerProblem);
        }

        var columnCount = headerFields!.Length;
        var index = headerFields.Select((name, i) => (name: name.Trim().TrimStart('﻿'), i)).Where(x => x.name.Length > 0).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        int exchangeAt = index["EXCH_ID"], segmentAt = index["SEGMENT"], instrumentAt = index["INSTRUMENT"], idAt = index["SECURITY_ID"];
        var rows = new List<MasterRow>(5000);
        var skipped = new List<BrokerIdentity>();
        var rowWarnings = 0;
        long read = 0;
        long outOfScope = 0;

        void Skip(string id, string? segment, string reason)
        {
            if (id.Length > 0 && segment is not null)
            {
                skipped.Add(new BrokerIdentity(InstrumentScope.BrokerSegment(segment), id));
            }

            rowWarnings++;
            if (rowWarnings <= MaxListedRowWarnings)
            {
                warnings.Add($"Skipped Dhan id {(id.Length > 0 && id.Length <= 40 ? Printable(id) : "?")}: {reason}.");
            }
        }

        while (true)
        {
            string? line;
            try
            {
                line = await csv.ReadLineAsync(cancellationToken);
            }
            catch (FormatException ex)
            {
                return Fail<MasterParse>($"Dhan's file is not a table near line {csv.RecordNumber + 1}: {ex.Message} Nothing was imported.");
            }

            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            read++;
            if (progress is not null && read % ProgressEvery == 0)
            {
                await progress(read);
            }

            // Cheap look at three columns of unquoted lines: most of the 207k rows are options and other exchanges.
            if (line.AsSpan().IndexOf('"') < 0
                && !InstrumentScope.MightBeInScope(FieldAt(line, exchangeAt), FieldAt(line, segmentAt), FieldAt(line, instrumentAt)))
            {
                outOfScope++;
                continue;
            }

            string[] fields;
            try
            {
                fields = await csv.SplitAsync(line, cancellationToken);
            }
            catch (FormatException ex)
            {
                return Fail<MasterParse>($"Dhan's file is malformed near line {csv.RecordNumber}: {ex.Message} Nothing was imported.");
            }

            var isNse = fields.Length > exchangeAt && fields[exchangeAt].Trim() == InstrumentScope.NseExchange;
            if (fields.Length != columnCount)
            {
                if (isNse)
                {
                    Skip(fields.Length > idAt ? fields[idAt].Trim() : string.Empty, fields.Length > segmentAt ? fields[segmentAt].Trim() : null,
                        $"line {csv.RecordNumber} has {fields.Length} columns, expected {columnCount}");
                }
                else
                {
                    outOfScope++;
                }

                continue;
            }

            var type = InstrumentScope.Classify(
                fields[exchangeAt].Trim(), fields[segmentAt].Trim(), fields[instrumentAt].Trim(), fields[index["INSTRUMENT_TYPE"]].Trim(),
                fields[index["UNDERLYING_SYMBOL"]], fields[index["SYMBOL_NAME"]], fields[index["DISPLAY_NAME"]]);
            if (type is null)
            {
                outOfScope++;
                continue;
            }

            var row = ReadRow(type.Value, fields, index, out var problem);
            if (row is null)
            {
                Skip(fields[idAt].Trim(), fields[segmentAt].Trim(), problem);
                continue;
            }

            rows.Add(row);
        }

        if (progress is not null)
        {
            await progress(read);
        }

        if (rowWarnings > MaxListedRowWarnings)
        {
            warnings.Add($"…and {rowWarnings - MaxListedRowWarnings} more rows skipped.");
        }

        if (rows.Count == 0)
        {
            return Fail<MasterParse>("Dhan's file contains no NSE shares, indices or futures. Nothing was imported.");
        }

        if (rowWarnings > MaxBadRows || rowWarnings * 100L > rows.Count + rowWarnings)
        {
            return Fail<MasterParse>($"Dhan's file has too many unreadable rows ({rowWarnings} of {rows.Count + rowWarnings}). Nothing was imported.");
        }

        return Result.Success(new MasterParse(rows, read, outOfScope, warnings, skipped));
    }

    /// <summary>Null when the importer can read the header (unknown or unused columns only add warnings to <paramref name="warnings"/>); otherwise what is wrong.</summary>
    internal static string? CheckHeader(string[]? header, List<string> warnings)
    {
        if (header is null)
        {
            return "Dhan's file is empty.";
        }

        var names = header.Select(h => h.Trim().TrimStart('﻿')).ToArray();
        var named = names.Where(n => n.Length > 0).ToArray();
        var missing = Required.Except(named, StringComparer.Ordinal).ToArray();
        var duplicated = named.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1 && Required.Contains(g.Key, StringComparer.Ordinal)).Select(g => g.Key).ToArray();
        if (missing.Length > 0 || duplicated.Length > 0)
        {
            var parts = new List<string>();
            if (missing.Length > 0)
            {
                parts.Add("missing " + Describe(missing));
            }

            if (duplicated.Length > 0)
            {
                parts.Add("repeated " + Describe(duplicated));
            }

            return "Dhan's file has changed shape (" + string.Join("; ", parts) + "). Nothing was imported.";
        }

        var unexpected = named.Except(Columns, StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
        {
            warnings.Add($"Dhan's file has {unexpected.Length} column(s) this version does not know ({Describe(unexpected)}); they are ignored.");
        }

        var unused = Columns.Except(Required, StringComparer.Ordinal).Except(named, StringComparer.Ordinal).ToArray();
        if (unused.Length > 0)
        {
            warnings.Add($"Dhan's file no longer has {unused.Length} column(s) that are not used ({Describe(unused)}).");
        }

        return null;
    }

    /// <summary>The field at <paramref name="column"/> of an unquoted line, trimmed; empty when the line is shorter.</summary>
    private static ReadOnlySpan<char> FieldAt(string line, int column)
    {
        var start = 0;
        for (var i = 0; i < column; i++)
        {
            var comma = line.AsSpan(start).IndexOf(',');
            if (comma < 0)
            {
                return default;
            }

            start += comma + 1;
        }

        var rest = line.AsSpan(start);
        var end = rest.IndexOf(',');
        return (end < 0 ? rest : rest[..end]).Trim();
    }

    private static string Describe(string[] names) =>
        names.Length <= 5 ? string.Join(", ", names.Select(Clean)) : string.Join(", ", names.Take(5).Select(Clean)) + $" and {names.Length - 5} more";

    private static string Clean(string name) => name.Length <= 40 ? Printable(name) : "(long name)";

    private static string Printable(string text) => new([.. text.Where(c => !char.IsControl(c))]);

    private static MasterRow? ReadRow(InstrumentType type, string[] f, Dictionary<string, int> ix, out string problem)
    {
        string Get(string column) => f[ix[column]].Trim();

        var segment = Get("SEGMENT");
        var securityId = Get("SECURITY_ID");
        var underlying = Get("UNDERLYING_SYMBOL");
        var symbolName = Get("SYMBOL_NAME");
        var displayName = Get("DISPLAY_NAME") is { Length: > 0 } display ? display : symbolName;
        var isin = Unavailable(Get("ISIN"));
        var series = Unavailable(Get("SERIES"));
        var flag = Flag(Get("ASM_GSM_FLAG"));
        var category = Unavailable(Get("ASM_GSM_CATEGORY"));

        if (securityId.Length == 0 || underlying.Length == 0 || symbolName.Length == 0)
        {
            problem = "security id, underlying symbol or symbol name is empty";
            return null;
        }

        var tooLong =
            TooLong("security id", securityId, InstrumentLimits.BrokerId) ?? TooLong("underlying symbol", underlying, InstrumentLimits.UnderlyingSymbol)
            ?? TooLong("symbol name", symbolName, InstrumentLimits.Name) ?? TooLong("display name", displayName, InstrumentLimits.DisplayName)
            ?? TooLong("ISIN", isin, InstrumentLimits.Isin) ?? TooLong("series", series, InstrumentLimits.Series)
            ?? TooLong("ASM/GSM flag", flag, InstrumentLimits.AsmGsmFlag) ?? TooLong("ASM/GSM category", category, InstrumentLimits.AsmGsmCategory);
        if (tooLong is not null)
        {
            problem = tooLong;
            return null;
        }

        if (!TryLot(Get("LOT_SIZE"), out var lot))
        {
            problem = "lot size is not a whole number from 1";
            return null;
        }

        DateOnly? expiry = null;
        if (type == InstrumentType.Future)
        {
            if (!DateOnly.TryParseExact(Get("SM_EXPIRY_DATE"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                problem = "expiry date of the future is not a date";
                return null;
            }

            expiry = date;
        }

        problem = string.Empty;
        return new MasterRow(
            type,
            segment,
            Get("INSTRUMENT"),
            securityId,
            isin,
            underlying,
            symbolName,
            displayName,
            series,
            lot,
            Decimal(Get("TICK_SIZE"), InstrumentLimits.TickSizeExclusiveMax),
            expiry,
            flag,
            category,
            Decimal(Get("MTF_LEVERAGE"), InstrumentLimits.MtfLeverageExclusiveMax),
            Whole(Get("SM_FREEZE_QTY")));
    }

    private static string? TooLong(string what, string? value, int max) => value is not null && value.Length > max ? $"{what} is longer than {max} characters" : null;

    /// <summary>Dhan writes "NA" (and sometimes nothing) where a value does not apply.</summary>
    private static string? Unavailable(string value) =>
        value.Length == 0 || value.Equals("NA", StringComparison.OrdinalIgnoreCase) ? null : value;

    /// <summary>"N" means not flagged.</summary>
    private static string? Flag(string value) => value.Equals("N", StringComparison.OrdinalIgnoreCase) ? null : Unavailable(value);

    private static bool TryLot(string text, out int lot)
    {
        lot = 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 1 || value > int.MaxValue || value != decimal.Truncate(value))
        {
            return false;
        }

        lot = (int)value;
        return true;
    }

    private static decimal? Decimal(string text, decimal exclusiveMax) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 && value < exclusiveMax ? value : null;

    private static long? Whole(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 && value == decimal.Truncate(value) && value <= long.MaxValue ? (long)value : null;

    private static Result<T> Fail<T>(string message) => Result.Failure<T>(Error.Upstream(FailureCode, message));
}
