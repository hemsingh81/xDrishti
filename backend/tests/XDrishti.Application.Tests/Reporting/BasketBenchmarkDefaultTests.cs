#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>BK-06c criteria 1 to 5 behind the ports: the basket benchmark (Nifty 500 by default, or the owner's choice) is what the profile and the ranking read; a missing index or missing bars is said, never replaced by Nifty 50.</summary>
public sealed class BasketBenchmarkDefaultTests
{
    private static readonly string From = ProfileWorld.Sessions[0].ToString("yyyy-MM-dd");
    private static readonly string To = ProfileWorld.Sessions[^1].ToString("yyyy-MM-dd");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ProfileWorld _world = new();

    private Guid Basket() => _world.Basket("Banks", null, _world.Instrument("A", i => 100 + i), _world.Instrument("B", i => 50 - (0.5 * i)), _world.Instrument("C", i => 100 + (0.5 * i)));

    private async Task<BasketProfileView> ProfileAsync(Guid basket, string? index = null)
    {
        var result = await _world.Profile().HandleAsync(new GetBasketProfileQuery(basket.ToString("D"), From, To, index, null), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public async Task Without_an_index_query_the_default_is_the_basket_benchmark_not_the_portfolio_benchmark()
    {
        var index = _world.Instrument("NSE:NIFTY_500", i => 1000 + (i * 100.0 / 24));
        _world.IndexIsBenchmark(index, "NSE:NIFTY_500", "Nifty 500");

        var view = await ProfileAsync(Basket());

        view.Index.ShouldBe(new ProfileIndexView(index, "NSE:NIFTY_500", true, "Nifty 500"));
        view.IndexDifference.Index.Number!.Value.ShouldBe(10.0, 1e-9);
        await _world.Benchmark.DidNotReceive().GetAsync(Arg.Any<CancellationToken>()); // the portfolio benchmark (Nifty 50) is not read
    }

    [Fact]
    public async Task The_owners_choice_is_the_index_and_not_the_default_and_the_query_wins_over_it()
    {
        var chosen = _world.Instrument("NSE:NIFTY_BANK", i => 1000 + (i * 100.0 / 24));
        _world.IndexIsBenchmark(chosen, "NSE:NIFTY_BANK", "Nifty Bank", isDefault: false);
        var other = _world.Instrument("NSE:NIFTY_100", i => 1000 + (i * 200.0 / 24));
        _world.Benchmark.FindAsync(other, Arg.Any<CancellationToken>()).Returns(new ValueTask<BenchmarkInstrument?>(new BenchmarkInstrument(other, "NSE:NIFTY_100", "Nifty 100", "index", false, true, true)));
        var basket = Basket();

        var own = await ProfileAsync(basket);
        var query = await ProfileAsync(basket, other.ToString("D"));

        (own.Index.Symbol, own.Index.IsDefault).ShouldBe(("NSE:NIFTY_BANK", false));
        (query.Index.Symbol, query.Index.IsDefault, query.Index.Name).ShouldBe(("NSE:NIFTY_100", false, "Nifty 100"));
        query.IndexDifference.Index.Number!.Value.ShouldBe(20.0, 1e-9);
    }

    [Fact]
    public async Task A_missing_nifty_500_leaves_every_benchmark_figure_unavailable_with_the_reason_and_falls_back_to_nothing()
    {
        // the world's default: Nifty 500 is not in the master; the portfolio benchmark (Nifty 50) exists with bars and must stay unused
        var nifty50 = _world.Instrument("NIFTY", i => 1000 + i);
        _world.Benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ConfiguredBenchmark?>(new ConfiguredBenchmark(new BenchmarkInstrument(nifty50, "NSE:NIFTY", "Nifty 50", "index", false, true, true), true)));

        var view = await ProfileAsync(Basket());

        view.Index.Symbol.ShouldBeNull();
        view.Hold.Number!.Value.ShouldBe(4.0, 1e-9);
        foreach (var m in new[] { view.Risk.Beta60, view.Risk.Beta252, view.Risk.Alpha, view.Risk.TrackingError, view.Risk.UpCapture, view.Risk.DownCapture, view.IndexDifference.Index, view.IndexDifference.Difference, view.Regime.Status })
        {
            m.State.ShouldBe("unavailable");
        }

        view.Risk.Beta252.Reason.ShouldBe("Nifty 500 is not in the instrument master");
        view.Series.Index.ShouldAllBe(v => v == null);
    }

    [Fact]
    public async Task A_missing_nifty_500_gives_the_ranking_states_and_no_rank()
    {
        var a = _world.Instrument("A", i => 100 + (3 * i));
        var b = _world.Instrument("B", i => 100 - i);
        var one = _world.Basket("One", null, a, b, _world.Instrument("C", i => 100 + i));
        var two = _world.Basket("Two", null, a, b, _world.Instrument("D", i => 100 - (2 * i)));

        var result = await _world.Ranking().HandleAsync(new GetBasketRankingQuery($"{one:D},{two:D}", From, To, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldAllBe(r => r.RsReturn21.State == "unavailable" && r.RsRank.Number == null);
        result.Value.RankWindow.ShouldBeNull();
        result.Value.Rows[0].RsReturn21.Reason.ShouldBe("Nifty 500 is not in the instrument master");
    }

    [Fact]
    public async Task Nifty_500_without_daily_bars_says_so_and_the_window_falls_back_to_the_calendar()
    {
        var index = _world.Instrument("NSE:NIFTY_500"); // in the master, no bars
        _world.IndexIsBenchmark(index, "NSE:NIFTY_500", "Nifty 500");

        var view = await ProfileAsync(Basket());
        var window = await _world.Resolver().ResolveAsync(null, null, null, Ct);

        view.IndexDifference.Index.State.ShouldBe("unavailable");
        view.Risk.Alpha.Reason.ShouldBe("Nifty 500 has no stored daily bars in this window");
        window.Value.View.FromSource.ShouldBe("calendar");
    }

    [Fact]
    public async Task A_change_of_the_default_index_is_not_served_from_the_cache_of_the_old_one()
    {
        var basket = Basket();
        var nifty50 = _world.Instrument("NSE:NIFTY", i => 1000 + (i * 100.0 / 24));
        _world.IndexIsBenchmark(nifty50, "NSE:NIFTY", "Nifty 50");
        var before = await ProfileAsync(basket);
        var nifty500 = _world.Instrument("NSE:NIFTY_500", i => 1000 + (i * 300.0 / 24));
        _world.IndexIsBenchmark(nifty500, "NSE:NIFTY_500", "Nifty 500");

        var after = await ProfileAsync(basket);

        before.IndexDifference.Index.Number!.Value.ShouldBe(10.0, 1e-9);
        after.Index.Symbol.ShouldBe("NSE:NIFTY_500");
        after.IndexDifference.Index.Number!.Value.ShouldBe(30.0, 1e-9);
    }
}
