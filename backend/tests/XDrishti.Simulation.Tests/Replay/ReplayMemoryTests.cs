using System.Globalization;
using System.Runtime.CompilerServices;
using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criterion 16 of P3-04: the memory of one replay. A synthetic instrument of about 190,000 bars (two years of minutes; 9.1 MB as a list of 48-byte bars) is replayed once; the bytes allocated in all, measured with
/// <c>GC.GetTotalAllocatedBytes(precise: true)</c>, are at most five times the size of the raw list. The bound was a design bound from the review of the spec, unverified against an implementation; the measured figure goes to the ADR.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class ReplayMemoryTests
{
    private const int Sessions = 507;

    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static long Allocated(Func<object> replay)
    {
        replay();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = replay();
        var after = GC.GetTotalAllocatedBytes(precise: true);
        GC.KeepAlive(result);
        return after - before;
    }

    [Fact]
    public void A_replay_of_190000_bars_allocates_at_most_five_times_the_size_of_the_raw_list()
    {
        var (bars, last) = SyntheticInstrument.Sessions(Sessions, seed: 7);
        var raw = (long)bars.Length * Unsafe.SizeOf<Bar>();
        bars.Length.ShouldBeInRange(189_000, 191_000);

        var allocated = Allocated(() => InstrumentReplayer.Replay(Bare, Rep.Input(bars, SyntheticInstrument.Start, last)));

        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:N0} bars = {1:N0} bytes raw; the replay allocated {2:N0} bytes = {3:F2} times the raw list", bars.Length, raw, allocated, (double)allocated / raw));
        allocated.ShouldBeLessThanOrEqualTo(5 * raw, "bytes allocated by one replay; raw list " + raw + " bytes");
    }

    [Fact]
    public void A_replay_that_adjusts_a_copy_stays_within_the_same_bound()
    {
        var (bars, last) = SyntheticInstrument.Sessions(Sessions, seed: 11);
        var raw = (long)bars.Length * Unsafe.SizeOf<Bar>();
        var split = Rep.Split(last, 1, 2);

        var allocated = Allocated(() => InstrumentReplayer.Replay(Bare, Rep.Input(bars, SyntheticInstrument.Start, last, factors: split)));

        TestContext.Current.TestOutputHelper?.WriteLine(string.Format(CultureInfo.InvariantCulture, "with an adjusted copy: {0:N0} bytes = {1:F2} times the raw list", allocated, (double)allocated / raw));
        allocated.ShouldBeLessThanOrEqualTo(5 * raw);
    }
}
