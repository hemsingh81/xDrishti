using System.Diagnostics;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>The preview of a report of about a thousand rows within the budget of P2-17 (a few hundred rows per file in at most 2 seconds).</summary>
public sealed class ReportPerformanceTests
{
    [Fact]
    public async Task A_thousand_row_report_is_previewed_in_two_seconds()
    {
        var s = await ReportScenario.StartAsync(x =>
        {
            x.Hold("ZENITHE", Zenith, 30, 12m);
            x.Hold("ORIONSTL", Orion, 30, 12m);
            x.ListSampleCompanies();
            for (var i = 0; i < 300; i++)
            {
                x.Master($"Company Number {i} Limited", $"CO{i}", $"INE{i:D3}A01011".Replace(' ', '0'));
            }
        });
        var names = new[] { "ZENITH ENERGY SYSTEM LTD", "ORION STEEL LTD.", "Unknown Thing One Ltd", "Company Number 7 Ltd", "FUT NIFTY 28 Apr 2026" };
        var rows = Enumerable.Range(0, 1000).Select(i => GtrRow(
            $"{1 + (i % 28):00} Apr 2026 00:00:00",
            names[i % names.Length] + (i % 5 == 2 ? " " + (i % 40) : string.Empty),
            "NSE",
            (100000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture),
            1 + (i % 50),
            $"{100 + i}.00",
            0,
            "0.00",
            brokerage: "1.00")).ToList();
        var text = Gtr(rows);
        await s.PreviewOkAsync(text);

        var watch = Stopwatch.StartNew();
        var preview = await s.PreviewOkAsync(text);
        watch.Stop();

        preview.Summary!.Rows.ShouldBe(1000);
        preview.Error.ShouldBeNull();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
