using System.Globalization;
using System.Text;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>Reads dividend file text or the committed made-up sample with the reader, at a fixed day (Thursday 2026-10-15, India).</summary>
internal static class DividendFiles
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public const string Header = "Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid";

    public static Stream Sample(string name) =>
        typeof(DividendFiles).Assembly.GetManifestResourceStream("Dividends/" + name) ?? throw new InvalidOperationException($"Sample {name} is not embedded.");

    public static async Task<Result<DividendFile>> ReadSampleAsync(string name)
    {
        await using var stream = Sample(name);
        return await DividendCsvReader.ReadAsync(stream, Today, CancellationToken.None);
    }

    public static async Task<Result<DividendFile>> ReadAsync(string text)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await DividendCsvReader.ReadAsync(stream, Today, CancellationToken.None);
    }

    public static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    public static string CsvWithHeader(string header, params string[] rows) => string.Join('\n', [header, .. rows]) + "\n";

    /// <summary>The refusal's text: the first field message (a refusal of a file names its field), else the message.</summary>
    public static string Text(Error error) => error.Fields is { Count: > 0 } fields ? fields[0].Message : error.Message;

    public static async Task<DividendFile> OkAsync(string header, params string[] rows)
    {
        var result = await ReadAsync(CsvWithHeader(header, rows));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    /// <summary>The one dividend of a file made of the standard header and one row.</summary>
    public static async Task<ParsedDividend> OneAsync(string row, string header = Header)
    {
        var file = await OkAsync(header, row);
        file.BadTotal.ShouldBe(0, string.Join("; ", file.Bad.Select(b => b.Reason)));
        return file.Items.ShouldHaveSingleItem();
    }

    public static async Task<string> BadReasonAsync(string row, string header = Header)
    {
        var file = await OkAsync(header, row);
        file.Items.ShouldBeEmpty();
        return file.Bad.ShouldHaveSingleItem().Reason;
    }

    /// <summary>A parsed row built by the reader's own rules (the key of the name included), for tests of the planner and the matcher.</summary>
    public static ParsedDividend Parsed(string name, string date = "2025-03-18", decimal amount = 100m, int line = 2, decimal? perShare = null, long? quantity = null, string? isin = null, string? symbol = null) =>
        new(line, DateOnly.Parse(date, CultureInfo.InvariantCulture), name, DividendNames.Key(name), isin, symbol, perShare, quantity, amount);

    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec"];

    /// <summary>A date the way Dhan's file writes it: <c>03 Jul 2026</c>, with <c>Sept</c> for September.</summary>
    public static string DhanDate(DateOnly date) => string.Create(CultureInfo.InvariantCulture, $"{date.Day:00} {Months[date.Month - 1]} {date.Year}");

    /// <summary>An amount the way Dhan's file writes it: two decimals, thousands grouped with commas (and quoted, as it holds a comma).</summary>
    public static string Money(decimal amount)
    {
        var text = amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return text.Contains(',', StringComparison.Ordinal) ? $"\"{text}\"" : text;
    }

    public static string Row(DateOnly date, string name, decimal perShare, long quantity, decimal paid) =>
        string.Create(CultureInfo.InvariantCulture, $"{DhanDate(date)},{name},{perShare},{quantity},{Money(paid)}");
}
