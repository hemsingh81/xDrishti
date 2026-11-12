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

    // The seam is 2024-04-02 09:15 IST; the cut is by date (CF-04d decision 8): India midnight of the seam date.
    private static readonly DateTimeOffset Seam = new(2024, 4, 2, 9, 15, 0, TimeSpan.FromMinutes(330));

    private static readonly long LastDay = new DateTimeOffset(2024, 4, 1, 15, 29, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static readonly long SeamDayEarly = new DateTimeOffset(2024, 4, 2, 9, 14, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static Bar At(long epoch) => new(epoch, 100, 101, 99, 100, 10);

    private static FakeBarStore Store()
    {
        var store = new FakeBarStore();
        store.Stored[Id] = [At(LastDay - 86_400), At(LastDay), At(SeamDayEarly), At(Seam.ToUnixTimeSeconds()), At(Seam.ToUnixTimeSeconds() + 60)];
        return store;
    }

    [Fact]
    public async Task Bars_dated_before_the_seam_date_are_left_out_and_the_seam_day_is_kept_whole()
    {
        var seams = new FakeSeams();
        seams.Cuts[Id] = Seam;

        var read = await SeamAwareBars.ReadAsync(Store(), seams, Id, From, To, Ct);

        read.Select(b => b.EpochSeconds).ShouldBe([SeamDayEarly, Seam.ToUnixTimeSeconds(), Seam.ToUnixTimeSeconds() + 60]);
    }

    [Fact]
    public async Task Without_a_seam_row_every_bar_is_read()
    {
        (await SeamAwareBars.ReadAsync(Store(), new FakeSeams(), Id, From, To, Ct)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task The_option_expression_spot_map_is_built_from_seam_aware_bars_only()
    {
        var seams = new FakeSeams();
        seams.Cuts[Id] = Seam;

        var spot = await RemarkShared.SpotAsync(Store(), seams, Id, From, To, Ct);

        spot.Keys.ShouldBe([SeamDayEarly, Seam.ToUnixTimeSeconds(), Seam.ToUnixTimeSeconds() + 60]);
    }
}
