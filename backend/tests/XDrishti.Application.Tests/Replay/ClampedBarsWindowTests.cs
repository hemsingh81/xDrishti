#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Replay;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-01 part B, the rolling window of the non-functional section: the bars of an instrument are read once per run and every cut is a slice that equals the read the old way (a window of its own per session), the clamp holds on every slice, and the window rolls.</summary>
public sealed class ClampedBarsWindowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private static readonly DateOnly First = new(2025, 1, 1);
    private static readonly DateOnly HoldOutStart = new(2029, 3, 1);
    private static readonly Guid Id = Guid.NewGuid();

    private static (ClampedBars Bars, IBarStore Store) Make(int barsPerSession = 30, DateTimeOffset? seam = null)
    {
        var holdOut = Substitute.For<IHoldOutStore>();
        holdOut.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<HoldOutBlock?>(new HoldOutBlock(HoldOutStart, First, First, 6, "h", DateTimeOffset.UnixEpoch)));
        var store = Substitute.For<IBarStore>();
        var all = new List<Bar>();
        for (var d = First; d < HoldOutStart.AddDays(30); d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var open = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), India).AddMinutes(555);
            for (var m = 0; m < barsPerSession; m++)
            {
                all.Add(new Bar(open.AddMinutes(m).ToUnixTimeSeconds(), 100 + m, 101, 99, 100, 10));
            }
        }

        store.ReadAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var (from, to) = (call.ArgAt<DateTimeOffset>(1).ToUnixTimeSeconds(), call.ArgAt<DateTimeOffset>(2).ToUnixTimeSeconds());
            return ValueTask.FromResult<IReadOnlyList<Bar>>([.. all.Where(b => b.EpochSeconds >= from && b.EpochSeconds <= to)]);
        });
        var seams = Substitute.For<IBackfillSeams>();
        seams.SeamOfAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(seam));
        return (new ClampedBars(holdOut, new HoldOutOptions(TrainingEmbargoDays: 30), store, seams, Substitute.For<IBarQueries>()), store);
    }

    [Fact]
    public async Task With_a_planned_range_the_bars_are_read_in_bounded_chunks_and_every_cut_equals_the_read_of_its_own_window_with_a_seam_and_a_backward_cut()
    {
        var seam = new DateTimeOffset(2026, 2, 2, 0, 0, 0, India);
        var (windowed, store) = Make(seam: seam);
        var (plain, _) = Make(seam: seam);
        var (from, last) = (new DateOnly(2026, 1, 5), new DateOnly(2028, 6, 30));
        windowed.PlanRange(last);

        async Task Same(DateOnly cut)
        {
            var a = await windowed.MinutesAsync(Id, cut, Ct);
            var b = await plain.MinutesAsync(Id, cut, Ct);
            a.Count.ShouldBe(b.Count, cut.ToString());
            a.SequenceEqual(b).ShouldBeTrue(cut.ToString());
            (a.Count == 0 || a[^1].EpochSeconds <= new DateTimeOffset(cut.AddDays(1).ToDateTime(TimeOnly.MinValue), India).ToUnixTimeSeconds()).ShouldBeTrue("no bar after the close of the cut");
            a.ShouldNotBeOfType<ArraySegment<Bar>>("a slice must not expose the array behind it");
            (a is IList<Bar>).ShouldBeFalse("a slice is read-only and cannot be cast to reach the window");
        }

        for (var cut = from; cut <= last; cut = cut.AddDays(1))
        {
            if (cut.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                await Same(cut);
            }
        }

        await Same(new DateOnly(2026, 3, 3)); // a cut before the start of the window reads its own window again

        var reads = store.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IBarStore.ReadAsync)).Select(c => (From: (DateTimeOffset)c.GetArguments()[1]!, To: (DateTimeOffset)c.GetArguments()[2]!)).ToList();
        reads.Count.ShouldBeLessThan(40, "chunks, not a read per session");
        reads.ShouldAllBe(r => (r.To - r.From).TotalDays <= ClampedBars.MinuteLookbackDays + 70, "no read is longer than the lookback plus one chunk");
    }

    [Fact]
    public async Task Without_a_planned_range_each_cut_reads_its_own_window_as_before()
    {
        var (bars, store) = Make();

        await bars.MinutesAsync(Id, new DateOnly(2026, 3, 2), Ct);
        await bars.MinutesAsync(Id, new DateOnly(2026, 3, 3), Ct);

        await store.Received(2).ReadAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cut_past_the_clamp_still_fails_closed_with_a_window_and_the_window_never_reads_past_the_clamp()
    {
        var (bars, store) = Make();
        var last = HoldOutStart.AddDays(-1 - 30);
        bars.PlanRange(HoldOutStart.AddDays(20)); // a horizon beyond the clamp

        await bars.MinutesAsync(Id, last.AddDays(-5), Ct);

        var to = (DateTimeOffset)store.ReceivedCalls().Single().GetArguments()[2]!;
        to.ShouldBeLessThanOrEqualTo(new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), India));
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.MinutesAsync(Id, last.AddDays(1), Ct));
    }
}
