#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Sizing;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Tests.Sizing;

/// <summary>
/// BK-10 criteria 11 and 12: the study reads stored closed trades and day bars only, uses the scale as of the previous close (no look-ahead: bars from the entry date on change nothing), never scales above 1, and counts what it could not scale or left out.
/// Series: 300 calm days (alternating +-0.5 %), then ten days of +-3 %; a trade entered the day after the volatile days is scaled down, one entered in the calm stretch is not.
/// </summary>
public sealed class GetSizingStudyHandlerTests
{
    private static readonly string Run = new('a', 64);

    private static readonly Guid Share = new("00000000-0000-0000-0000-0000000000d1");

    private static readonly DateOnly Day0 = new(2025, 1, 1);

    private static DateOnly Day(int i) => Day0.AddDays(i);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<BarRow> Bars(int days, Func<int, double> dailyReturn)
    {
        var rows = new List<BarRow>();
        var close = 100.0;
        for (var i = 0; i < days; i++)
        {
            close *= 1 + dailyReturn(i);
            rows.Add(new BarRow(new DateTimeOffset(Day(i).ToDateTime(new TimeOnly(15, 30)), TimeSpan.FromHours(5.5)), close, close, close, close, 1000, 1, true, "test"));
        }

        return rows;
    }

    private static double Calm(int i) => i % 2 == 0 ? 0.005 : -0.005;

    private static double CalmThenWild(int i) => i < 300 ? Calm(i) : (i % 2 == 0 ? 0.03 : -0.03);

    private static ReportTrade Trade(string key, int entryDay, double? net) =>
        new(Run, key, "S", "1D", "BUY", Share, "AAA", new([]), Day(entryDay), Day(entryDay + 1), net ?? 0, net, 1);

    private static GetSizingStudyHandler Handler(IReadOnlyList<ReportTrade> trades, IReadOnlyList<BarRow> bars, SizingSettings? settings = null)
    {
        var source = Substitute.For<IReportTradeSource>();
        source.StreamTradesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<ReportTradeFilter>(), Arg.Any<CancellationToken>()).Returns(_ => trades.ToAsyncEnumerable());
        var barQueries = Substitute.For<IBarQueries>();
        barQueries.ReadDailyBarsAsync(Share, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult<IReadOnlyList<BarRow>>([.. bars.Where(b => DateOnly.FromDateTime(b.Ts.DateTime) < call.ArgAt<DateOnly>(2))]));
        var store = Substitute.For<ISizingStore>();
        store.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new SizingState(settings ?? SizingSettings.Defaults, 0)));
        return new GetSizingStudyHandler(source, barQueries, store, NullLogger<GetSizingStudyHandler>.Instance);
    }

    private static GetSizingStudyQuery Query(double? floor = null, string? run = null) => new(run ?? Run, null, null, floor);

    [Fact]
    public async Task A_trade_after_volatile_days_is_scaled_down_and_one_in_the_calm_stretch_is_not()
    {
        var trades = new[] { Trade("a", 250, 1), Trade("b", 311, -1) };

        var view = (await Handler(trades, Bars(400, CalmThenWild)).HandleAsync(Query(), Ct)).Value;

        view.Result.Trades.ShouldBe(2);
        view.WithoutScale.ShouldBe(0);
        view.Result.UnscaledTotalR.ShouldBe(0.0, 1e-9);
        view.Result.ScaledTotalR.ShouldBeGreaterThan(0.0); // the losing trade was scaled down, the winner was not
        view.Result.ScaledTotalR.ShouldBeLessThan(1.0);
        view.Result.ScaledMaxDrawdownR.ShouldBeLessThan(view.Result.UnscaledMaxDrawdownR);
        view.FloorSource.ShouldBe("setting");
        view.Floor.ShouldBe(0.5);
        view.Caveat.ShouldContain("descriptive, before costs, same trades");
    }

    [Fact]
    public async Task Truncation_bars_from_the_entry_date_on_change_nothing()
    {
        var trades = new[] { Trade("a", 250, 1), Trade("b", 311, -1) };
        var plain = (await Handler(trades, Bars(400, CalmThenWild)).HandleAsync(Query(), Ct)).Value;
        // The same history up to the day before the last entry, then a very different future.
        var rewritten = Bars(400, i => i < 311 ? CalmThenWild(i) : (i % 2 == 0 ? 0.2 : -0.2));

        var other = (await Handler(trades, rewritten).HandleAsync(Query(), Ct)).Value;

        other.Result.ScaledTotalR.ShouldBe(plain.Result.ScaledTotalR, 1e-12);
        other.Result.ScaledMaxDrawdownR.ShouldBe(plain.Result.ScaledMaxDrawdownR, 1e-12);
    }

    [Fact]
    public async Task A_share_with_too_little_history_is_taken_at_scale_one_and_counted()
    {
        var trades = new[] { Trade("a", 30, 1), Trade("b", 40, -1) };

        var view = (await Handler(trades, Bars(400, CalmThenWild)).HandleAsync(Query(), Ct)).Value;

        view.WithoutScale.ShouldBe(2);
        view.Result.ScaledTotalR.ShouldBe(view.Result.UnscaledTotalR, 1e-12);
    }

    [Fact]
    public async Task Closed_trades_with_no_net_r_are_left_out_and_counted()
    {
        var trades = new[] { Trade("a", 250, 1), Trade("b", 260, null) };

        var view = (await Handler(trades, Bars(400, CalmThenWild)).HandleAsync(Query(), Ct)).Value;

        view.SkippedNoNetR.ShouldBe(1);
        view.Result.Trades.ShouldBe(1);
    }

    [Fact]
    public async Task The_study_is_never_more_than_the_unscaled_trades_it_cannot_scale_up()
    {
        var trades = Enumerable.Range(0, 40).Select(i => Trade("t" + i, 200 + (i * 3), i % 2 == 0 ? 1 : -1)).ToArray();

        var view = (await Handler(trades, Bars(400, CalmThenWild)).HandleAsync(Query(), Ct)).Value;

        view.Result.ScaledMaxDrawdownR.ShouldBeLessThanOrEqualTo(view.Result.UnscaledMaxDrawdownR + 1e-9);
    }

    [Theory]
    [InlineData("nothex")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_bad_run_key_is_reports_invalid(string? run)
    {
        var result = await Handler([], []).HandleAsync(new GetSizingStudyQuery(run, null, null, null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task A_floor_other_than_the_three_labelled_values_is_reports_invalid_and_each_of_them_is_a_labelled_sensitivity()
    {
        var handler = Handler([Trade("a", 250, 1)], Bars(400, CalmThenWild));

        (await handler.HandleAsync(Query(0.4), Ct)).Error.Code.ShouldBe("reports.invalid");
        foreach (var floor in new[] { 0.25, 0.5, 0.75 })
        {
            var view = (await handler.HandleAsync(Query(floor), Ct)).Value;
            view.FloorSource.ShouldBe("sensitivity");
            view.Floor.ShouldBe(floor);
        }
    }

    [Fact]
    public async Task A_from_after_to_is_reports_invalid()
    {
        var result = await Handler([], []).HandleAsync(new GetSizingStudyQuery(Run, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), null), Ct);

        result.Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task Without_a_floor_asked_for_the_stored_floor_is_used()
    {
        var view = (await Handler([Trade("a", 250, 1)], Bars(400, CalmThenWild), SizingSettings.Defaults with { Floor = 0.75 }).HandleAsync(Query(), Ct)).Value;

        view.Floor.ShouldBe(0.75);
    }
}
