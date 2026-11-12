using System.Globalization;
using System.Text;
using XDrishti.Api.CostSheets;
using XDrishti.Api.IntegrationTests.Simulate;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.CostSheets;

/// <summary>CS-01: the two delete paths of the derive output. The host is built without a database (derive reads files and writes files).</summary>
public sealed class DeriveCostSheetFilesTests
{
    private static readonly Dictionary<string, string?> NoDatabase = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=1",
        ["BrokerVault:Key"] = string.Empty,
    };

    private static string Report()
    {
        var rows = Enumerable.Range(1, 10).Select(d => string.Join(',', new[]
        {
            d.ToString("00", CultureInfo.InvariantCulture) + " Apr 2026 00:00:00", "ZZ SCRIP", "NSE", "ZZBILL" + d, "100", "10000.00", "0", "0.00", "0.00", "0.07", "10.00", "0.10", "1.50", "0.30", "0.10", "0.00",
        }.Select(c => "\"" + c + "\"")));
        return string.Join('\n',
        [
            "Global transction report,From 01-04-2026 to 15-04-2026", "Name,ZZ", "UCC,1000000001", "Mobile,ZZ", "Email ID,zz@example.invalid", string.Empty,
            "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Brokerage,GST,STT,SEBI Fees,Stamp Duty,Txn. Charges,Oth. Charges,Gross Amount",
            .. rows, string.Empty, "Net P&L,0.00,Brokerage,0.00,Gross P&L,0.00,Total Charges,0.00", string.Empty, "NOTE : invented",
        ]) + "\n";
    }

    private static async Task<(int Exit, string Error)> DeriveAsync(string report, string output, CancellationToken cancellationToken)
    {
        using var host = SimulateHost.Create(NoDatabase, serviceName: "xd-cost-sheet");
        var error = new StringWriter();
        var exit = await CostSheetCommand.RunAsync(["derive", report, "--out", output], host.Services, new SimulateConsole(new StringWriter(), error), cancellationToken);
        return (exit, error.ToString());
    }

    [Fact]
    public async Task When_the_residuals_cannot_be_written_the_sheet_just_written_is_deleted()
    {
        using var temp = new TempFolder();
        var report = Path.Combine(temp.Path, "report.csv");
        await File.WriteAllTextAsync(report, Report(), new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var output = Path.Combine(temp.Path, "out", "derived.json");
        Directory.CreateDirectory(Path.Combine(temp.Path, "out"));
        // a directory where the residuals file must go: it does not "exist" as a file, but it cannot be opened as a new one
        Directory.CreateDirectory(output + ".residuals.csv");

        var (exit, error) = await DeriveAsync(report, output, TestContext.Current.CancellationToken);

        exit.ShouldBe(1, error);
        File.Exists(output).ShouldBeFalse("a sheet is never left without its residuals");
        Directory.Exists(output + ".residuals.csv").ShouldBeTrue("what was not ours is not touched");
    }

    [Fact]
    public async Task A_file_that_is_already_there_is_never_deleted_by_a_failed_write()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "existing.csv");
        await File.WriteAllTextAsync(path, "mine", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<IOException>(async () => await CostSheetCommand.WriteOwnerOnlyAsync(path, "new", TestContext.Current.CancellationToken));

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("mine");
    }

    [Fact]
    public async Task A_write_that_fails_after_our_own_open_deletes_the_partial_file()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "partial.json");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await CostSheetCommand.WriteOwnerOnlyAsync(path, "new", cancelled.Token));

        File.Exists(path).ShouldBeFalse();
    }
}
