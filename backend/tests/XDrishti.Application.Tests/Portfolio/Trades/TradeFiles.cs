using System.Text;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>Reads tradebook text or the committed made-up samples with the reader, at a fixed day (Tuesday 2026-10-13, India).</summary>
internal static class TradeFiles
{
    public static readonly DateOnly Today = new(2026, 10, 13);

    public const string Header = "date,symbol,isin,side,quantity,price";

    public static Stream Sample(string name) =>
        typeof(TradeFiles).Assembly.GetManifestResourceStream("Trades/" + name) ?? throw new InvalidOperationException($"Sample {name} is not embedded.");

    public static async Task<Result<TradeFile>> ReadSampleAsync(string name)
    {
        await using var stream = Sample(name);
        return await TradeCsvReader.ReadAsync(stream, Today, CancellationToken.None);
    }

    public static async Task<Result<TradeFile>> ReadAsync(string text)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await TradeCsvReader.ReadAsync(stream, Today, CancellationToken.None);
    }

    /// <summary>The file with the given header and rows, one per line.</summary>
    public static Task<Result<TradeFile>> ReadRowsAsync(string header, params string[] rows) => ReadAsync(string.Join('\n', [header, .. rows]) + "\n");

    public static async Task<TradeFile> OkAsync(string header, params string[] rows)
    {
        var result = await ReadRowsAsync(header, rows);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    /// <summary>The one trade of a file made of a header and one row.</summary>
    public static async Task<ParsedTrade> OneAsync(string row, string header = Header)
    {
        var file = await OkAsync(header, row);
        file.BadTotal.ShouldBe(0, string.Join("; ", file.Bad.Select(b => b.Reason)));
        return file.Trades.ShouldHaveSingleItem();
    }

    public static async Task<string> BadReasonAsync(string row, string header = Header)
    {
        var file = await OkAsync(header, row);
        file.Trades.ShouldBeEmpty();
        return file.Bad.ShouldHaveSingleItem().Reason;
    }
}
