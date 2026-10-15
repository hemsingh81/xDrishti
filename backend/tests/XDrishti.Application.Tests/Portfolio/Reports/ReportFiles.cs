using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>Reads report text or the committed made-up sample into the sheets and files the readers and handlers work on, at a fixed day (Thursday 2026-10-15, India). Nothing here is the owner's data.</summary>
internal static class ReportFiles
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    /// <summary>The client code (UCC) of the made-up samples. It is not the account's Dhan client id: the application never compares the two.</summary>
    public const string ClientCode = "1000000001";

    public const string GtrTitle = "Global transction report,From 01-04-2026 to 15-10-2026";

    public const string GtrHeader = "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Brokerage,GST,STT,SEBI Fees,Stamp Duty,Txn. Charges,Oth. Charges,Gross Amount";

    public const string DematHeader = "Scrip Name,ISIN Code,Free Holding,Locked In,Safe Keep,MTF Pledge,Margin Pledge,CUSA Pledge,Closing Price,Valuation";

    public static Stream Sample(string name) =>
        typeof(ReportFiles).Assembly.GetManifestResourceStream("Reports/" + name) ?? throw new InvalidOperationException($"Sample {name} is not embedded.");

    public static string SampleText(string name)
    {
        using var reader = new StreamReader(Sample(name), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static TradeFileContent Content(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>The text as a sheet, read the way an uploaded CSV is.</summary>
    public static async Task<Sheet> SheetAsync(string text) => Ok(await SheetLoader.LoadAsync(Content(text), new FakeSpreadsheets(), CancellationToken.None));

    public static Task<Sheet> SampleSheetAsync(string name) => SheetAsync(SampleText(name));

    public static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    /// <summary>A global transaction report: title, personal lines (with markers the tests look for), header, the rows, a footer with the total charges of the rows and a note.</summary>
    public static string Gtr(IEnumerable<string> rows, string code = ClientCode, decimal? footerCharges = null)
    {
        var list = rows.ToList();
        var total = footerCharges ?? list.Sum(r => ChargesOf(r));
        return string.Join('\n', [GtrTitle, "Name,ZZ-SENTINEL-NAME", $"UCC,{code}", "Mobile,ZZ-SENTINEL-MOBILE", "Email ID,ZZ-SENTINEL-EMAIL@example.invalid", string.Empty, GtrHeader, .. list, string.Empty, string.Create(CultureInfo.InvariantCulture, $"Net P&L,0.00,Brokerage,0.00,Gross P&L,0.00,Total Charges,{total:0.00}"), string.Empty, "NOTE : This sheet was downloaded at 15-10-2026 10:00:00"]) + "\n";
    }

    /// <summary>A row of the report as the CSV writes it (every field in quotes).</summary>
    public static string GtrRow(string date, string name, string exchange, string bill, long buyQty, string buyValue, long sellQty, string sellValue, string brokerage = "0.00", string gst = "0.00", string stt = "0.00", string sebi = "0.00", string stamp = "0.00", string txn = "0.00", string other = "0.00")
    {
        string[] cells = [date, name, exchange, bill, buyQty.ToString(CultureInfo.InvariantCulture), buyValue, sellQty.ToString(CultureInfo.InvariantCulture), sellValue, brokerage, gst, stt, sebi, stamp, txn, other, "0.00"];
        return string.Join(',', cells.Select(c => "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
    }

    /// <summary>The sum of the seven charge fields of a row written by <see cref="GtrRow"/> (a field that is no number counts as nothing).</summary>
    public static decimal ChargesOf(string row)
    {
        var cells = row.Split("\",\"").Select(c => c.Trim('"')).ToArray();
        return cells.Skip(8).Take(7).Sum(c => decimal.TryParse(c.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture, out var part) ? part : 0m);
    }

    public static string Dp(IEnumerable<string> lines, string title = "DP Charges | From 01-April-2026 to 15-October-2026") =>
        string.Join('\n', [title, "Name,ZZ-SENTINEL-NAME,UCC," + ClientCode + ",Mobile,ZZ-SENTINEL-MOBILE", string.Empty, "Sr.,Date,ISIN,Security Name,,,,Quantity,Buy/Sell,Type of Transaction,,Charges", .. lines, "Total,,,,,,,,,,,12.5", "This is a system generated report and does not require signature", "Generated on 15-10-2026"]) + "\n";

    public static string DpLine(int sr, string date, string isin, string name, string quantity, string side, string type, string charge) =>
        $" {sr},{date},{isin}, {name},,,,{quantity},{side},{type},,{charge}";

    public static string Demat(IEnumerable<string> rows, string code = ClientCode, string asOf = "15-10-2026") =>
        string.Join('\n', [$"Holding summary,For {asOf}", "Name,ZZ-SENTINEL-NAME", $"UCC,{code}", "Mobile,ZZ-SENTINEL-MOBILE", "Email ID,ZZ-SENTINEL-EMAIL@example.invalid", string.Empty, DematHeader, .. rows, "Valuation,0.00,Total Number of Securities,0"]) + "\n";

    public static string DematRow(string name, string isin, string free, string locked = "0", string safeKeep = "0", string mtf = "0", string margin = "0", string cusa = "0", string price = "100.00", string valuation = "0.00") =>
        string.Join(',', [$"\"{name}\"", isin, free, locked, safeKeep, mtf, margin, cusa, price, valuation]);
}

/// <summary>The spreadsheet port with rows a test chooses (a stand-in for the library; the real reader is tested in the infrastructure tests).</summary>
internal sealed class FakeSpreadsheets : ISpreadsheetReader
{
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = [];

    public int Reads { get; private set; }

    public ValueTask<Result<IReadOnlyList<IReadOnlyList<string>>>> ReadAsync(Stream content, int maxRows, CancellationToken cancellationToken)
    {
        Reads++;
        return ValueTask.FromResult(Result.Success(Rows));
    }
}
