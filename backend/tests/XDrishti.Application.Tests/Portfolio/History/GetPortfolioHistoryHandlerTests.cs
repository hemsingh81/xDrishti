using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Actions;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;
using static XDrishti.Application.Tests.Portfolio.History.HistoryFixture;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>The history use case on in-memory ports (acceptance criteria 1, 5, 6 and 8 of P2-07).</summary>
public sealed class GetPortfolioHistoryHandlerTests
{
    private readonly HistoryFixture _fx = new();

    private void TwoYearsOfNothing() => _fx.StoreBenchmark((5, 1000), (6, 1100), (7, 1200), (8, 900), (9, 1000));

    [Fact]
    public async Task The_answer_has_the_points_the_coverage_the_benchmark_and_the_drawdowns()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5), Lot(5, 110m, 7));
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100));
        TwoYearsOfNothing();

        var history = await _fx.OkAsync();

        history.Points.Select(p => (p.Date.Day, p.Value, p.Invested, p.BenchmarkValue, p.Nav, p.BenchmarkNav)).ShouldBe(
        [
            (5, 1000m, 1000m, (decimal?)1000m, 100m, (decimal?)100m),
            (6, 1100m, 1000m, 1100m, 110m, 110m),
            (7, 1800m, 1550m, 1750m, 120m, 120m),
            (8, 1350m, 1550m, 1312.5m, 90m, 90m),
            (9, 1500m, 1550m, 1458.3333m, 100m, 100m),
        ]);
        history.Coverage.ShouldBe(new HistoryCoverage(1, 1, 0, 0));
        history.Partial.ShouldBeFalse();
        history.Benchmark.ShouldBe(new HistoryBenchmark("ok", _fx.Nifty.Id, "NSE:NIFTY", "Nifty 50", true, true, null, Oct(9)));
        history.MaxDrawdown.ShouldBe(new Drawdown(25m, Oct(7), Oct(8)));
        history.BenchmarkMaxDrawdown.ShouldBe(new Drawdown(25m, Oct(7), Oct(8)));
        history.FirstDate.ShouldBe(Oct(5));
        history.LastPriceAt.ShouldBeNull();
        history.Resolution.ShouldBe("day");
        (history.RangeFrom, history.RangeTo).ShouldBe((Oct(5), Oct(9)));
    }

    [Fact]
    public async Task All_the_bars_come_from_one_read_for_every_instrument_and_the_benchmark_from_before_the_earliest_lot()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Add(Beta, Lot(10, 100m, 6));
        _fx.Add(Alpha, Lot(1, 100m, 7));
        _fx.Store(Alpha, (5, 100), (9, 100));
        _fx.Store(Beta, (5, 100), (9, 100));
        TwoYearsOfNothing();

        await _fx.OkAsync();

        _fx.Prices.Calls.ShouldBe(1);
        var read = _fx.Prices.Last.ShouldNotBeNull();
        read.Ids.ShouldBe([Alpha, Beta, _fx.Nifty.Id], ignoreOrder: true);
        read.From.ShouldBe(Oct(5).AddDays(-GetPortfolioHistoryHandler.LookbackDays));
        read.To.ShouldBe(Oct(10));
        _fx.Holdings.ListCalls.ShouldBe(1);
    }

    [Fact]
    public async Task The_scope_is_the_account_or_the_bucket_asked_for()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Holdings.Holdings.Add(HoldingFixtures.Holding(100m, "OTH", "open", Beta, Lot(20, 100m, 5)) with { AccountId = OtherAccount, BucketId = OtherBucket });
        _fx.Store(Alpha, (5, 100), (6, 100));
        _fx.Store(Beta, (5, 100), (6, 100));

        var all = await _fx.OkAsync();
        var account = await _fx.OkAsync(Request(account: OtherAccount));
        var bucket = await _fx.OkAsync(Request(bucket: HoldingFixtures.Bucket));

        all.Points[0].Value.ShouldBe(3000m);
        account.Points[0].Value.ShouldBe(2000m);
        bucket.Points[0].Value.ShouldBe(1000m);
    }

    [Fact]
    public async Task An_unknown_account_or_bucket_gives_an_empty_history()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100));

        var unknown = await _fx.OkAsync(Request(account: Guid.NewGuid()));

        unknown.Points.ShouldBeEmpty();
        unknown.Coverage.ShouldBe(new HistoryCoverage(0, 0, 0, 0));
        unknown.FirstDate.ShouldBeNull();
        unknown.MaxDrawdown.ShouldBeNull();
        unknown.Benchmark.State.ShouldBe("unavailable");
        _fx.Prices.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Holdings_without_bars_are_counted_and_the_history_is_partial()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Add(Beta, Lot(10, 100m, 5));
        _fx.Add(null, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));
        TwoYearsOfNothing();

        var history = await _fx.OkAsync();

        history.Coverage.ShouldBe(new HistoryCoverage(3, 1, 2, 0));
        history.Partial.ShouldBeTrue();
        history.Points[0].Value.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_benchmark_without_bars_is_unavailable_with_the_reason_and_the_tracking_state()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));
        var untracked = _fx.Benchmarks.Add("NSE:NIFTYBEES", "Other index", tracked: false, hasBars: false);
        _fx.Benchmarks.Chosen = untracked.Id;

        var history = await _fx.OkAsync();

        history.Benchmark.State.ShouldBe("unavailable");
        history.Benchmark.Tracked.ShouldBeFalse();
        history.Benchmark.HasBars.ShouldBeFalse();
        history.Benchmark.Symbol.ShouldBe("NSE:NIFTYBEES");
        history.Benchmark.Reason.ShouldNotBeNull().ShouldContain("Track it");
        history.Points.ShouldAllBe(p => p.BenchmarkValue == null && p.BenchmarkNav == null);
        history.BenchmarkMaxDrawdown.ShouldBeNull();
    }

    [Fact]
    public async Task A_tracked_benchmark_without_bars_asks_for_Update_data()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));

        var history = await _fx.OkAsync();

        history.Benchmark.State.ShouldBe("unavailable");
        history.Benchmark.Reason.ShouldNotBeNull().ShouldContain("Update data");
    }

    [Fact]
    public async Task A_benchmark_that_starts_after_the_first_investment_says_when_it_starts()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));
        _fx.StoreBenchmark((7, 1000), (9, 1100));

        var history = await _fx.OkAsync();

        history.Benchmark.State.ShouldBe("unavailable");
        history.Benchmark.Reason.ShouldNotBeNull().ShouldContain("7 Oct 2026");
    }

    [Fact]
    public async Task Without_a_benchmark_index_in_the_master_the_history_still_has_value_and_invested()
    {
        _fx.Benchmarks.Default = null;
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));

        var history = await _fx.OkAsync();

        history.Benchmark.ShouldBe(new HistoryBenchmark("unavailable", null, null, null, false, false, "The benchmark index is not in the instrument master yet. Refresh the instrument list.", null));
        history.Points.Count.ShouldBe(5);
    }

    [Fact]
    public async Task From_and_to_cut_the_range_without_rebasing_the_nav_and_the_drawdown_is_of_the_range()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100));
        TwoYearsOfNothing();

        var history = await _fx.OkAsync(Request(from: "2026-10-07", to: "2026-10-09"));

        history.Points.Select(p => (p.Date.Day, p.Nav)).ShouldBe([(7, 120m), (8, 90m), (9, 100m)]);
        history.MaxDrawdown.ShouldBe(new Drawdown(25m, Oct(7), Oct(8)));
        history.FirstDate.ShouldBe(Oct(5)); // the first day of the whole history, not of the range
    }

    [Fact]
    public async Task A_range_after_the_peak_does_not_see_the_earlier_fall()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100));

        var history = await _fx.OkAsync(Request(from: "2026-10-08"));

        history.MaxDrawdown.ShouldBe(new Drawdown(0m, null, null));
    }

    [Fact]
    public async Task Week_and_month_resolutions_keep_the_last_trading_day_of_the_period_but_the_drawdown_stays_daily()
    {
        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.Zero));
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100), (12, 101), (13, 102));

        var weekly = await _fx.OkAsync(Request(resolution: "week"));
        var monthly = await _fx.OkAsync(Request(resolution: "month"));

        weekly.Points.Select(p => p.Date.Day).ShouldBe([9, 13]);
        weekly.Resolution.ShouldBe("week");
        monthly.Points.Select(p => p.Date.Day).ShouldBe([13]);
        monthly.MaxDrawdown.ShouldBe(new Drawdown(25m, Oct(7), Oct(8)));
    }

    [Fact]
    public async Task A_long_history_is_thinned_automatically_and_a_resolution_that_would_exceed_the_limit_is_refused()
    {
        _fx.Add(Alpha, HoldingFixtures.Open(10, 100m, new DateOnly(2015, 1, 1)));
        var closes = new List<(DateOnly, double)>();
        for (var date = new DateOnly(2015, 1, 1); date <= Oct(9); date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                closes.Add((date, 100.0));
            }
        }

        _fx.Prices.Set(Alpha, closes);

        var automatic = await _fx.OkAsync();
        var refused = await _fx.HistoryAsync(Request(resolution: "day"));
        var explicitWeek = await _fx.OkAsync(Request(resolution: "week"));

        closes.Count.ShouldBeGreaterThan(GetPortfolioHistoryHandler.MaxPoints);
        automatic.Resolution.ShouldBe("week");
        automatic.Points.Count.ShouldBeLessThanOrEqualTo(GetPortfolioHistoryHandler.MaxPoints);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("portfolio.invalid");
        refused.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["resolution"]);
        explicitWeek.Points.Count.ShouldBe(automatic.Points.Count);
    }

    [Theory]
    [InlineData("2026-13-01", null, null, "from")]
    [InlineData("10/05/2026", null, null, "from")]
    [InlineData(null, "yesterday", null, "to")]
    [InlineData("1989-12-31", null, null, "from")]
    [InlineData("2026-10-09", "2026-10-05", null, "to")]
    [InlineData(null, null, "year", "resolution")]
    public async Task Bad_input_is_portfolio_invalid_with_the_field(string? from, string? to, string? resolution, string field)
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));

        var result = await _fx.HistoryAsync(Request(from: from, to: to, resolution: resolution));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldContain(field);
        _fx.Holdings.ListCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_split_applied_to_the_instrument_makes_the_pre_ex_date_values_continuous()
    {
        _fx.Add(Alpha, Lot(20, 100m, 5));
        _fx.Prices.Set(Alpha, [(Oct(5), 200.0), (Oct(6), 210.0), (Oct(7), 105.0), (Oct(8), 110.0)], new AppliedAction(Oct(7), CorporateActionKind.Split, 1, 2, 0.5m));

        var history = await _fx.OkAsync();

        history.Points.Select(p => p.Value).ShouldBe([2000m, 2100m, 2100m, 2200m]);
    }

    [Fact]
    public async Task Today_s_point_uses_the_prices_of_today_and_matches_the_totals_of_the_holdings()
    {
        _fx.Clock.SetUtcNow(MondayMorning);
        var priced = HoldingFixtures.Holding(130m, "AAA", "open", Alpha, Lot(10, 100m, 5)) with { LastPriceAt = MondayMorning };
        var other = HoldingFixtures.Holding(55m, "BBB", "open", Beta, Lot(4, 50m, 6)) with { LastPriceAt = MondayMorning.AddMinutes(-3) };
        _fx.Holdings.Holdings.AddRange([priced, other]);
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100));
        _fx.Store(Beta, (5, 50), (6, 50), (7, 50), (8, 50), (9, 50));
        TwoYearsOfNothing();

        var history = await _fx.OkAsync();

        var last = history.Points[^1];
        last.Date.ShouldBe(Oct(12));
        var totals = ReturnsCalculator.ForGroup(_fx.Holdings.Holdings, Oct(12));
        last.Value.ShouldBe(totals.Value.ShouldNotBeNull());
        last.Invested.ShouldBe(totals.Invested);
        history.LastPriceAt.ShouldBe(MondayMorning);
    }

    [Fact]
    public async Task There_is_no_live_point_before_the_session_opens_nor_on_a_holiday_nor_from_old_prices()
    {
        var priced = HoldingFixtures.Holding(130m, "AAA", "open", Alpha, Lot(10, 100m, 5)) with { LastPriceAt = MondayMorning };
        _fx.Holdings.Holdings.Add(priced);
        _fx.Store(Alpha, (5, 100), (9, 100));

        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 3, 30, 0, TimeSpan.Zero)); // 09:00 IST, 15 minutes before the open
        var early = await _fx.OkAsync();
        _fx.Cache.Invalidate();
        _fx.Clock.SetUtcNow(MondayMorning);
        _fx.Calendar = WithHoliday(Oct(12));
        var holiday = await _fx.OkAsync();
        _fx.Cache.Invalidate();
        _fx.Calendar = Weekdays;
        _fx.Holdings.Holdings[0] = priced with { LastPriceAt = MondayMorning.AddDays(-1) };
        var stale = await _fx.OkAsync();

        early.Points[^1].Date.ShouldBe(Oct(9));
        holiday.Points[^1].Date.ShouldBe(Oct(9));
        stale.Points[^1].Date.ShouldBe(Oct(9)); // yesterday's price would only repeat the last close
        stale.LastPriceAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_answer_is_cached_per_key_until_something_invalidates_it()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));

        var first = await _fx.OkAsync();
        var second = await _fx.OkAsync();
        await _fx.OkAsync(Request(resolution: "week"));
        _fx.Cache.Invalidate();
        await _fx.OkAsync();

        second.ShouldBeSameAs(first);
        _fx.Prices.Calls.ShouldBe(3); // day (computed), week (another key), day again after the invalidation
        _fx.Cache.Hits.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_request_is_not_cached_and_a_result_computed_during_an_invalidation_is_not_stored()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (9, 100));
        await _fx.HistoryAsync(Request(resolution: "year"));

        _fx.Cache.Sets.ShouldBe(0);
        var stale = _fx.Cache.Version;
        _fx.Cache.Invalidate();
        _fx.Cache.Set("key", new object(), stale);
        _fx.Cache.Sets.ShouldBe(0);
    }

    [Fact]
    public async Task A_closed_holding_s_sale_ends_its_value_in_the_history()
    {
        _fx.Holdings.Holdings.Add(HoldingFixtures.Holding(100m, "OLD", "closed", Alpha, Sold(10, 100m, 5, 8, 95m)));
        _fx.Store(Alpha, (5, 100), (6, 110), (7, 120), (8, 90), (9, 100));

        var history = await _fx.OkAsync();

        history.Points.Select(p => p.Value).ShouldBe([1000m, 1100m, 1200m, 0m, 0m]);
        history.Coverage.ShouldBe(new HistoryCoverage(0, 0, 0, 0));
    }

    [Fact]
    public async Task On_a_live_day_the_portfolio_moves_and_the_benchmark_stays_at_the_last_close_with_no_flat_point_made_up()
    {
        // Monday morning: ALPHA is quoted 10 % above Friday's close, the index has no bar for today
        _fx.Clock.SetUtcNow(MondayMorning);
        var quoted = HoldingFixtures.Holding(110m, "AAA", "open", Alpha, Lot(10, 100m, 5)) with { LastPriceAt = MondayMorning };
        _fx.Holdings.Holdings.Add(quoted);
        _fx.Store(Alpha, (5, 100), (6, 100), (7, 100), (8, 100), (9, 100));
        _fx.StoreBenchmark((5, 1000), (6, 1000), (7, 1000), (8, 1000), (9, 1000));

        var history = await _fx.OkAsync();

        var live = history.Points[^1];
        (live.Date, live.Value, live.Nav).ShouldBe((Oct(12), 1100m, 110m));
        (live.BenchmarkValue, live.BenchmarkNav).ShouldBe((null, null)); // nothing is made up for a day the index has not closed
        history.Points[^2].BenchmarkValue.ShouldBe(1000m);
        history.Benchmark.AsOf.ShouldBe(Oct(9));
        history.Benchmark.State.ShouldBe("ok");
        history.BenchmarkMaxDrawdown.ShouldBe(new Drawdown(0m, null, null)); // measured on the days the benchmark has
        history.LastPriceAt.ShouldBe(MondayMorning);
        history.RangeTo.ShouldBe(Oct(12));
    }

    [Fact]
    public async Task The_benchmark_drawdown_ignores_the_live_day_after_the_last_close()
    {
        _fx.Clock.SetUtcNow(MondayMorning);
        _fx.Holdings.Holdings.Add(HoldingFixtures.Holding(110m, "AAA", "open", Alpha, Lot(10, 100m, 5)) with { LastPriceAt = MondayMorning });
        _fx.Store(Alpha, (5, 100), (6, 100), (7, 100), (8, 100), (9, 100));
        _fx.StoreBenchmark((5, 1000), (6, 1200), (7, 1200), (8, 900), (9, 1000));

        var history = await _fx.OkAsync();

        history.BenchmarkMaxDrawdown.ShouldBe(new Drawdown(25m, Oct(6), Oct(8)));
        history.Points.Where(p => p.BenchmarkNav is not null).Select(p => p.Date.Day).ShouldBe([5, 6, 7, 8, 9]);
    }

    [Fact]
    public async Task A_benchmark_that_ends_before_the_first_investment_is_unavailable_with_the_reason()
    {
        _fx.Add(Alpha, Lot(10, 100m, 7));
        _fx.Store(Alpha, (7, 100), (8, 100), (9, 100));
        _fx.StoreBenchmark((5, 1000), (6, 1000));

        var history = await _fx.OkAsync();

        history.Benchmark.State.ShouldBe("unavailable");
        history.Benchmark.AsOf.ShouldBeNull();
        history.Benchmark.Reason.ShouldNotBeNull().ShouldContain("before your first investment");
        history.Points.ShouldAllBe(p => p.BenchmarkValue == null);
    }

    [Fact]
    public async Task The_latest_price_label_needs_a_price_of_today_that_belongs_to_a_covered_instrument()
    {
        // BETA is quoted today but has no bars (not covered); ALPHA has bars but only an old price: no live point, no "latest price" time
        _fx.Clock.SetUtcNow(MondayMorning);
        _fx.Holdings.Holdings.AddRange(
        [
            HoldingFixtures.Holding(110m, "AAA", "open", Alpha, Lot(10, 100m, 5)),
            HoldingFixtures.Holding(55m, "BBB", "open", Beta, Lot(4, 50m, 5)) with { LastPriceAt = MondayMorning },
        ]);
        _fx.Store(Alpha, (5, 100), (9, 100));

        var history = await _fx.OkAsync();

        history.LastPriceAt.ShouldBeNull();
        history.Points[^1].Date.ShouldBe(Oct(9));
        history.Coverage.ShouldBe(new HistoryCoverage(2, 1, 1, 0));
    }

    [Fact]
    public async Task A_request_that_would_read_too_many_daily_prices_is_refused_before_anything_is_read()
    {
        // 300 instruments over about 31 years are far beyond the limit
        for (var i = 0; i < 300; i++)
        {
            _fx.Add(Guid.NewGuid(), HoldingFixtures.Open(1, 1m, new DateOnly(1995, 1, 2)));
        }

        var result = await _fx.HistoryAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("scope");
        _fx.Prices.Calls.ShouldBe(0);
        _fx.Cache.Sets.ShouldBe(0);
    }

    [Fact]
    public async Task The_range_fields_are_the_first_and_last_day_returned_and_empty_without_points()
    {
        _fx.Add(Alpha, Lot(10, 100m, 5));
        _fx.Store(Alpha, (5, 100), (6, 100), (7, 100), (8, 100), (9, 100));

        var cut = await _fx.OkAsync(Request(from: "2026-10-06", to: "2026-10-08", resolution: "week"));
        var none = await _fx.OkAsync(Request(from: "2026-11-01"));

        (cut.RangeFrom, cut.RangeTo).ShouldBe((Oct(6), Oct(8)));
        (none.RangeFrom, none.RangeTo).ShouldBe((null, null));
    }
}
