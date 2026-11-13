#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Lab;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.HoldOut;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Replay.HoldOut;

/// <summary>SR-03 AC 7: with a claimed block's reader the replay reads up to the block end and not a bar past it; without one the training clamp still refuses the block.</summary>
public sealed class HoldOutBlockReaderTests
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);
    private static readonly DateOnly First = new(2026, 1, 1);
    private static readonly DateOnly BlockStart = new(2027, 5, 5);
    private static readonly DateOnly BlockEnd = new(2027, 11, 4);
    private static readonly Guid Id = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HoldOutBlockReader Reader()
    {
        var registration = Guid.NewGuid();
        var look = new StoredUnlock(HoldOutRegistrationIds.LookKey(registration), 1, "system", "owner", "owner:x", DateTimeOffset.UnixEpoch, new string('a', 64), false, BlockStart, Guid.NewGuid(),
            new HoldOutResultView("x", 1, "system", "owner", "used", DateTimeOffset.UnixEpoch, BlockStart, BlockEnd, false, false, false, "inconclusive", "", null, null, null, 0, null, new string('a', 64), "owner:x", []));
        return new HoldOutBlockReader(HoldOutClaim.From(look, registration).Value, BlockEnd);
    }

    private static (ClampedBars Bars, List<DateTimeOffset> Reads) Make(HoldOutBlockReader? reader)
    {
        var holdOut = Substitute.For<IHoldOutStore>();
        holdOut.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<HoldOutBlock?>(new HoldOutBlock(BlockStart, First, BlockEnd, 6, "h", DateTimeOffset.UnixEpoch, new DateOnly(2026, 11, 5))));
        var reads = new List<DateTimeOffset>();
        var store = Substitute.For<IBarStore>();
        store.ReadAsync(Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            reads.Add(call.ArgAt<DateTimeOffset>(2));
            return ValueTask.FromResult<IReadOnlyList<Bar>>([]);
        });
        var seams = Substitute.For<IBackfillSeams>();
        seams.SeamOfAsync(Id, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<DateTimeOffset?>(null));
        return (new ClampedBars(holdOut, new HoldOutOptions(TrainingEmbargoDays: 30), store, seams, Substitute.For<IBarQueries>(), 40, reader), reads);
    }

    [Fact]
    public async Task With_the_reader_no_read_ends_after_the_close_of_the_block_end_whatever_the_cut_and_a_later_cut_throws()
    {
        var (bars, reads) = Make(Reader());
        var closeOfEnd = new DateTimeOffset(BlockEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), India);
        var random = new Random(7);
        bars.PlanRange(BlockEnd);

        for (var i = 0; i < 200; i++)
        {
            var cut = BlockStart.AddDays(random.Next(0, (BlockEnd.DayNumber - BlockStart.DayNumber) + 1));
            await bars.MinutesAsync(Id, cut, Ct);
        }

        reads.ShouldNotBeEmpty();
        reads.ShouldAllBe(r => r <= closeOfEnd);
        (await bars.LastReadableAsync(Ct)).ShouldBe(BlockEnd);
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.MinutesAsync(Id, BlockEnd.AddDays(1), Ct));
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.DaysAsync(Id, BlockEnd, BlockEnd.AddDays(1), Ct));
    }

    [Fact]
    public async Task Without_the_reader_the_training_clamp_refuses_every_cut_in_the_block()
    {
        var (bars, reads) = Make(null);

        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.MinutesAsync(Id, BlockStart, Ct));
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await bars.MinutesAsync(Id, BlockEnd, Ct));

        reads.ShouldBeEmpty();
        (await bars.LastReadableAsync(Ct)).ShouldBeLessThan(BlockStart);
    }
}
