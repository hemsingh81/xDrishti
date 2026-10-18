using System.Diagnostics;
using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// The explicit benchmark of P3-04 (Non-functional), not part of <c>scripts/check.sh</c>; run with <c>--explicit only</c>. Replays 20 invented seeded instruments of two years (507 sessions, 190,125 minutes) one at a time, as a run would, and
/// prints, per instrument, the time of the hash of the bars and of the adjustment of a copy, the time of the whole replay, the bytes allocated, and the peak working set of the process. Reported and not asserted: the read is P3-04a's, and the
/// figure for the 214 instruments of the owner is first measured on his data by P3-04b.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ReplayBenchmarks
{
    private const int Instruments = 20;

    private const int Sessions = 507;

    [Fact(Explicit = true)]
    public void Replay_phases_for_synthetic_instruments()
    {
        var strategy = Rep.Prepare(OrbV1.Create());
        var report = new List<string>();
        double hashTotal = 0, adjustTotal = 0, replayTotal = 0;
        long allocatedTotal = 0;
        for (var i = 0; i < Instruments; i++)
        {
            var (bars, last) = SyntheticInstrument.Sessions(Sessions, seed: (ulong)(100 + i));
            var factors = Rep.Split(last, 1, 2);
            var scan = SessionScan.Classify(bars, Rep.DefaultCalendar, ReplayOptions.Default);

            var clock = Stopwatch.StartNew();
            var hash = BarStreamHash.Compute(bars);
            var hashMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var copy = ReplayBars.Build(bars, scan.Ran, factors);
            var adjustMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var replay = InstrumentReplayer.Replay(strategy, Rep.Input(bars, SyntheticInstrument.Start, last));
            var replayMs = clock.Elapsed.TotalMilliseconds;
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            hashTotal += hashMs;
            adjustTotal += adjustMs;
            replayTotal += replayMs;
            allocatedTotal += allocated;
            report.Add(string.Format(CultureInfo.InvariantCulture, "#{0}: hash {1:F1} ms, adjust {2:F1} ms, replay {3:F0} ms, {4:N0} bytes allocated, {5} trades, {6} candidates (hash {7}, {8} fed bars)", i, hashMs, adjustMs, replayMs, allocated, replay.Trades.Count, replay.Record.Fates.Total, hash[..8], copy.Fed.Count));
        }

        using var process = Process.GetCurrentProcess();
        var peak = Math.Max(process.PeakWorkingSet64, process.WorkingSet64);
        var committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
        report.Add(string.Format(
            CultureInfo.InvariantCulture,
            "mean per instrument: hash {0:F1} ms, adjust {1:F1} ms, replay {2:F0} ms, {3:N0} bytes allocated; peak working set of the process {4:N0} MB, GC heap committed {6:N0} MB; 214 instruments at the replay rate: {5:F0} s",
            hashTotal / Instruments,
            adjustTotal / Instruments,
            replayTotal / Instruments,
            allocatedTotal / Instruments,
            peak / (1024 * 1024),
            replayTotal / Instruments * 214 / 1000,
            committed / (1024 * 1024)));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", report));
    }
}
