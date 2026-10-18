using System.Diagnostics;
using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Exports a run to a temporary root through the real library and folder writer and reports the bytes of every file, the time of each phase and the memory (the figures the explicit benchmarks of P3-04b1 print).</summary>
internal static class ExportReport
{
    public static async Task WriteAsync(SimulationRun run, StringBuilder report, CancellationToken ct)
    {
        using var temp = new TempFolder();
        var rules = FillRules.Default;
        var labels = RunLabels.Of(run, rules);
        var runId = RunId.Of(run.Strategy.Id, run.From, run.To, run.RunKey);
        var timings = new List<(string Name, double Seconds)>();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        ExportFile Timed(string name, Action<System.Buffers.IBufferWriter<byte>> render) => new(name, sink =>
        {
            var watch = Stopwatch.StartNew();
            render(sink);
            timings.Add((name, watch.Elapsed.TotalSeconds));
        });

        ExportFile[] files =
        [
            Timed("README.txt", sink => ExportFixture.Text("README.txt", RunReadme.Text(labels, runId)).Render(sink)),
            Timed("open_at_end.csv", sink => TradesCsv.Write(sink, run.RunKey, run.OpenAtEnd)),
            Timed("run.json", sink => SimExportJson.Write(sink, run, labels, "xd-sim-export-1")),
            Timed("trades.csv", sink => TradesCsv.Write(sink, run.RunKey, run.Trades)),
        ];

        var watch = Stopwatch.StartNew();
        var first = await new ExportFolderWriter().WriteAsync(
            temp.Root,
            runId,
            files,
            entries => (ExportManifest.Build("xd-sim-export-1", run.RunKey, run.Universe.BasketId, entries, []), ExportKey.Of("xd-sim-export-1", run.RunKey, run.Universe.BasketId, entries, [])),
            force: false,
            ct);
        var writeSeconds = watch.Elapsed.TotalSeconds;
        first.IsSuccess.ShouldBeTrue();

        watch.Restart();
        var again = await new ExportFolderWriter().WriteAsync(
            temp.Root,
            runId,
            files,
            entries => (ExportManifest.Build("xd-sim-export-1", run.RunKey, run.Universe.BasketId, entries, []), ExportKey.Of("xd-sim-export-1", run.RunKey, run.Universe.BasketId, entries, [])),
            force: false,
            ct);
        var unchangedSeconds = watch.Elapsed.TotalSeconds;
        again.Value.Status.ShouldBe(ExportStatus.Unchanged);

        foreach (var entry in first.Value.Entries)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-16} {1,15:N0} bytes ({2:F1} MB)", entry.Name, entry.Bytes, entry.Bytes / 1048576.0));
        }

        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  total            {0,15:N0} bytes ({1:F1} MB)", first.Value.Entries.Sum(e => e.Bytes), first.Value.Entries.Sum(e => e.Bytes) / 1048576.0));
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  render: {0}", string.Join(", ", timings.Take(4).Select(t => t.Name + " " + t.Seconds.ToString("F2", CultureInfo.InvariantCulture) + " s"))));
        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  write (render, hash, flush to disk, rename): {0:F2} s; the same input again, hashing every file on disk (unchanged): {1:F2} s", writeSeconds, unchangedSeconds));

        using var process = Process.GetCurrentProcess();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  memory: {0:N0} MB allocated during the first write and the check (the record itself is in memory, {1:N0} MB at the start); peak working set of the test process {2:N0} MB (the larger of the peak and the current figure; macOS may report no peak)",
            allocated / 1048576,
            heapBefore / 1048576,
            Math.Max(process.PeakWorkingSet64, process.WorkingSet64) / 1048576));
    }
}
