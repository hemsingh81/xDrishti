using System.Diagnostics;
using System.Globalization;
using System.Text;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// The explicit benchmark of P3-04b1 for the upper bound of the spec (E4): a synthetic record of 217,000 trades (214 instruments x 507 sessions x 2 sides) is exported through the real library and folder writer, and the bytes of every file, the time of each
/// phase and the memory are printed. A selective rule gives far fewer trades than the bound; the bound is what the design must hold. Not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>; needs no database.
/// </summary>
public sealed class ExportSizeBenchmarks
{
    [Fact(Explicit = true)]
    public async Task Export_phases_for_the_upper_bound_of_trades()
    {
        const int Trades = 217_000;
        var clock = Stopwatch.StartNew();
        var record = FabricatedRun.Large(Trades);
        var report = new StringBuilder();
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "synthetic record: {0:N0} trades (the bound of the spec: 214 instruments x 507 sessions x 2 sides), built in {1:F1} s", Trades, clock.Elapsed.TotalSeconds));

        await ExportReport.WriteAsync(record, report, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }
}
