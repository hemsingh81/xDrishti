using XDrishti.Application.Market.History;
using XDrishti.Application.OptionExpression;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04 Part B, ADR 0270 decision 10: the one read every simulation, learning and report path uses leaves out the bars before the high-water seam.</summary>
public sealed class SeamAwareBarsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Id = Guid.NewGuid();

    private static readonly DateTimeOffset From = DateTimeOffset.UnixEpoch;

    private static readonly DateTimeOffset To = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Bar At(long epoch) => new(epoch, 100, 101, 99, 100, 10);

    private static FakeBarStore Store()
    {
        var store = new FakeBarStore();
        store.Stored[Id] = [At(1_000), At(1_060), At(1_120)];
        return store;
    }

    [Fact]
    public async Task Bars_before_the_seam_are_left_out_and_the_seam_minute_stays()
    {
        var seams = new FakeSeams();
        seams.Cuts[Id] = DateTimeOffset.FromUnixTimeSeconds(1_060);

        var read = await SeamAwareBars.ReadAsync(Store(), seams, Id, From, To, Ct);

        read.Select(b => b.EpochSeconds).ShouldBe([1_060L, 1_120L]);
    }

    [Fact]
    public async Task Without_a_seam_row_every_bar_is_read()
    {
        (await SeamAwareBars.ReadAsync(Store(), new FakeSeams(), Id, From, To, Ct)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_option_expression_spot_map_is_built_from_seam_aware_bars_only()
    {
        var seams = new FakeSeams();
        seams.Cuts[Id] = DateTimeOffset.FromUnixTimeSeconds(1_120);

        var spot = await RemarkShared.SpotAsync(Store(), seams, Id, From, To, Ct);

        spot.Keys.ShouldBe([1_120L]);
    }
}
