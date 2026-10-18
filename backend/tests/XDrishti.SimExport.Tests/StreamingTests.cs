using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests;

/// <summary>Criterion 12 of P3-04b1 (the library half): rendering a 200,000-trade record to a sink allocates a bounded amount, well under the size of the output, because no copy of the file is built.</summary>
public sealed class StreamingTests
{
    private const int Trades = 200_000;

    private static readonly Lazy<SimulationRun> Run = new(() => FabricatedRun.Large(Trades));

    [Fact]
    public void The_csv_of_200000_trades_allocates_a_small_fraction_of_its_size()
    {
        var run = Run.Value;
        var sink = new CountingSink();
        TradesCsv.Write(new CountingSink(), run.RunKey, run.Trades.Take(1000).ToList());

        var before = GC.GetAllocatedBytesForCurrentThread();
        TradesCsv.Write(sink, run.RunKey, run.Trades);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestContext.Current.SendDiagnosticMessage(string.Format(CultureInfo.InvariantCulture, "trades.csv of {0:N0} trades: {1:N0} bytes written, {2:N0} bytes allocated", Trades, sink.Bytes, allocated));
        run.Trades.Count.ShouldBe(Trades);
        sink.Bytes.ShouldBeGreaterThan(50_000_000);
        allocated.ShouldBeLessThan(sink.Bytes / 100, "the rows are built in a reused buffer and the sink is the host's");
    }

    [Fact]
    public void The_json_of_200000_trades_allocates_a_small_fraction_of_its_size()
    {
        var run = Run.Value;
        var labels = RunLabels.Of(run, FillRules.Default);
        var sink = new CountingSink();
        SimExportJson.Write(new CountingSink(), FabricatedRun.Standard(), RunLabels.Of(FabricatedRun.Standard(), FillRules.Default), "xd-sim-export-1");

        var before = GC.GetAllocatedBytesForCurrentThread();
        SimExportJson.Write(sink, run, labels, "xd-sim-export-1");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestContext.Current.SendDiagnosticMessage(string.Format(CultureInfo.InvariantCulture, "run.json of {0:N0} trades: {1:N0} bytes written, {2:N0} bytes allocated", Trades, sink.Bytes, allocated));
        sink.Bytes.ShouldBeGreaterThan(80_000_000);
        allocated.ShouldBeLessThan(sink.Bytes / 4, "a string per trade for the key is the only copy; the file itself is never built");
    }
}
