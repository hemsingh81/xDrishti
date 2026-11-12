#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Application.Tests.Market.History;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Research;

/// <summary>CF-04 Part B criterion 11 (fail closed): bars before an instrument's seam are Dhan's raw backfill and never enter a run, whichever instrument.</summary>
public sealed class InstrumentInputReaderSeamTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 1, 9, 15, 0, IndiaTime.Offset);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Bar At(DateTimeOffset when) => new(when.ToUnixTimeSeconds(), 100, 101, 99, 100, 10);

    private static async Task<int> ReadCountAsync(DateTimeOffset? seam)
    {
        var id = Guid.NewGuid();
        var store = new FakeBarStore();
        store.Stored[id] = [At(Seam.AddDays(-30)), At(Seam.AddDays(-1)), At(Seam), At(Seam.AddMinutes(1))];
        var adjustments = Substitute.For<IAdjustmentFactors>();
        adjustments.GetAsync(id, Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.None);
        var seams = new FakeSeams();
        if (seam is { } cut)
        {
            seams.Cuts[id] = cut;
        }


        var read = await new InstrumentInputReader(store, Substitute.For<IBarQueries>(), adjustments, seams).ReadAsync(id, new DateOnly(2026, 4, 30), null, Ct);
        return read.Bars.Count;
    }

    [Fact]
    public async Task Bars_before_the_recorded_seam_are_left_out_and_the_seam_bar_stays()
    {
        (await ReadCountAsync(Seam)).ShouldBe(2);
    }

    [Fact]
    public async Task An_instrument_without_a_seam_row_reads_every_bar_as_before()
    {
        (await ReadCountAsync(null)).ShouldBe(4);
    }
}
