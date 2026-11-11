#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Collections;
using System.Reflection;
using System.Text.Json;
using NSubstitute;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>
/// BK-07 behind the ports, on the seeded fixture worked by hand: A runs 100 to 124 (+24 %), B 50 to 38 (-24 %), C 100 to 112 (+12 %) over 25 sessions, so the basket's hold is +4 %; the index runs 1000 to 1100 (+10 %), so the passive difference is -6 points.
/// </summary>
public sealed class GetBasketProfileHandlerTests
{
    private static readonly string From = ProfileWorld.Sessions[0].ToString("yyyy-MM-dd");
    private static readonly string To = ProfileWorld.Sessions[^1].ToString("yyyy-MM-dd");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ProfileWorld _world = new();

    private (Guid Basket, Guid A, Guid B, Guid C, Guid Index) Seeded(bool withIndex = true)
    {
        var a = _world.Instrument("A", i => 100 + i);
        var b = _world.Instrument("B", i => 50 - (0.5 * i));
        var c = _world.Instrument("C", i => 100 + (0.5 * i));
        var index = _world.Instrument("NIFTY", i => 1000 + (i * 100.0 / 24));
        if (withIndex)
        {
            _world.IndexIsBenchmark(index);
        }

        return (_world.Basket("Banks", null, a, b, c), a, b, c, index);
    }

    private async Task<BasketProfileView> OkAsync(Guid basket, string? from = null, string? to = null, string? index = null, string? asOf = null)
    {
        var result = await _world.Profile().HandleAsync(new GetBasketProfileQuery(basket.ToString("D"), from ?? From, to ?? To, index, asOf), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    private async Task<Error> RefusedAsync(string? id, string? from = null, string? to = null)
    {
        var result = await _world.Profile().HandleAsync(new GetBasketProfileQuery(id, from ?? From, to ?? To, null, null), Ct);
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    [Fact]
    public async Task The_seeded_fixture_gives_the_hand_worked_hold_index_return_and_difference_and_the_series_start_at_100()
    {
        var (basket, _, _, _, _) = Seeded();

        var view = await OkAsync(basket);

        view.Hold.State.ShouldBe("value");
        view.Hold.Number!.Value.ShouldBe(4.0, 1e-9);
        view.IndexDifference.Index.Number!.Value.ShouldBe(10.0, 1e-9);
        view.IndexDifference.Difference.Number!.Value.ShouldBe(-6.0, 1e-9);
        view.Members.Total.ShouldBe(3);
        view.Members.Counted.ShouldBe(3);
        view.Members.Excluded.ShouldBeEmpty();
        view.Index.Symbol.ShouldBe("NIFTY");
        view.Index.IsDefault.ShouldBeTrue();
        view.Series.Dates.Count.ShouldBe(25);
        view.Series.Basket[0].ShouldBe(100.0);
        view.Series.Index[0].ShouldBe(100.0);
        view.Series.Index[^1]!.Value.ShouldBe(110.0, 1e-9);
        view.Series.Drawdown.Count.ShouldBe(25);
        view.Window.Source.ShouldBe("query");
    }

    [Fact]
    public async Task The_basket_of_three_has_no_breadth_dispersion_or_correlation_and_says_why_with_n_and_the_minimum()
    {
        var (basket, _, _, _, _) = Seeded();

        var view = await OkAsync(basket);

        view.Breadth.B20.Share.State.ShouldBe("insufficient");
        view.Breadth.B20.Share.Number.ShouldBeNull();
        view.Breadth.B20.Share.Reason.ShouldBe("n = 3, at least 5 needed");
        view.Dispersion.Today.State.ShouldBe("insufficient");
        view.Correlation.RhoBar.State.ShouldBe("insufficient");
        view.Correlation.RhoBar.Number.ShouldBeNull();
        view.Correlation.Matrix.ShouldBeNull();
        view.Breadth.SeriesB20.ShouldAllBe(v => v == null);
    }

    [Fact]
    public async Task A_basket_of_four_is_insufficient_with_n_4_and_min_5()
    {
        var (_, a, b, c, _) = Seeded();
        var four = _world.Basket("Four", null, a, b, c, _world.Instrument("D", i => 100 + (i * 2)));

        var view = await OkAsync(four);

        view.Breadth.B20.Share.Reason.ShouldBe("n = 4, at least 5 needed");
        view.Breadth.B50.Share.Reason.ShouldBe("n = 0, at least 5 needed");
        view.Dispersion.Today.Reason.ShouldBe("n = 4, at least 5 needed");
        view.Correlation.NEff.Reason.ShouldBe("n = 4, at least 5 needed");
        view.Correlation.Matrix.ShouldBeNull();
    }

    [Fact]
    public async Task A_basket_with_no_bars_is_empty_and_names_every_member_as_no_bars()
    {
        var (_, _, _, _, _) = Seeded();
        var empty = _world.Basket("Empty", null, _world.Instrument("X"), _world.Instrument("Y"), _world.Instrument("Z"));

        var view = await OkAsync(empty);

        view.Hold.State.ShouldBe("empty");
        view.Hold.Number.ShouldBeNull();
        view.Members.Excluded.Select(e => (e.Symbol, e.Reason)).ShouldBe([("X", "no_bars"), ("Y", "no_bars"), ("Z", "no_bars")]);
        view.Members.Counted.ShouldBe(0);
        view.Series.Dates.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_benchmark_bars_every_figure_against_the_index_is_unavailable_with_where_the_owner_can_track_it()
    {
        var (basket, _, _, _, _) = Seeded(withIndex: false);

        var view = await OkAsync(basket);

        view.Index.Symbol.ShouldBeNull();
        foreach (var m in new[] { view.Risk.Beta60, view.Risk.Beta252, view.Risk.Alpha, view.Risk.TrackingError, view.Risk.InformationRatio, view.Risk.UpCapture, view.Risk.DownCapture, view.IndexDifference.Index, view.IndexDifference.Difference })
        {
            m.State.ShouldBe("unavailable");
            m.Number.ShouldBeNull();
            m.Reason!.ShouldBe("Nifty 500 is not in the instrument master");
        }

        view.RelativeStrength.ShouldAllBe(r => r.Return.State == "unavailable");
        view.Series.Index.ShouldAllBe(v => v == null);
        view.Evidence.UnavailableInputs.ShouldContain(s => s.StartsWith("benchmark index", StringComparison.Ordinal));
        view.Hold.Number!.Value.ShouldBe(4.0, 1e-9);
    }

    [Fact]
    public async Task Without_regime_codes_the_regime_panel_is_unavailable_with_the_reason()
    {
        var (basket, _, _, _, _) = Seeded();

        var view = await OkAsync(basket);

        view.Regime.Status.State.ShouldBe("unavailable");
        view.Regime.Rows.ShouldBeEmpty();
        view.Evidence.UnavailableInputs.ShouldContain(s => s.StartsWith("regime", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_single_snapshot_says_so_in_the_basis_and_the_limits_and_a_dated_basket_gives_the_first_date()
    {
        var (basket, a, b, c, _) = Seeded();
        var dated = _world.Basket("Dated", null, a, b, c);
        _world.Stay(dated, _world.Instrument("D", i => 100 + i), ProfileWorld.At(ProfileWorld.Sessions[3]));

        var single = await OkAsync(basket);
        var withHistory = await OkAsync(dated);

        single.MembershipBasis.ShouldBe("single_snapshot");
        single.Evidence.Limits.ShouldContain(l => l.Contains("not as of each trading day; survivorship applies (ISS-22)", StringComparison.Ordinal));
        withHistory.MembershipBasis.ShouldBe("dated");
        withHistory.MembershipFrom.ShouldBe(new DateOnly(2026, 6, 1));
        withHistory.Evidence.Limits.ShouldContain(l => l.Contains("dated from 2026-06-01", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_member_added_on_session_3_weighs_in_from_the_next_session_the_previous_sessions_membership()
    {
        var up1 = _world.Instrument("A", i => 100 * Math.Pow(1.01, i));
        var up2 = _world.Instrument("B", i => 100 * Math.Pow(1.01, i));
        var up3 = _world.Instrument("C", i => 100 * Math.Pow(1.01, i));
        var late = _world.Instrument("D", i => 100 * Math.Pow(1.02, i));
        _world.IndexIsBenchmark(_world.Instrument("NIFTY", i => 1000 + i));
        var basket = _world.Basket("Grows", null, up1, up2, up3);
        _world.Stay(basket, late, ProfileWorld.At(ProfileWorld.Sessions[3]));

        var view = await OkAsync(basket);

        // days 1 to 3 use the three members (+1 %), days 4 to 24 use four (the mean of 1, 1, 1 and 2 %)
        var expected = 100 * Math.Pow(1.01, 3) * Math.Pow(1.0125, 21);
        view.Series.Basket[^1].ShouldBe(expected, 1e-9);
        view.Members.Total.ShouldBe(4);
        view.Turnover.Turnover.State.ShouldBe("value");
        view.Turnover.Adds.ShouldBe(1);
    }

    [Fact]
    public async Task An_instrument_day_with_a_minus_50_percent_jump_is_left_out_and_named_and_an_excluded_member_changes_no_figure()
    {
        var (basket, a, b, c, _) = Seeded();
        var jump = _world.Instrument("E", i => i < 12 ? 100 : 50);
        var plain = await OkAsync(basket);
        var four = _world.Basket("WithJump", null, a, b, c, jump, _world.Instrument("F"));

        var view = await OkAsync(four);

        view.Members.Total.ShouldBe(5);
        view.Members.Excluded.Select(e => (e.Symbol, e.Reason)).ShouldContain(("E", "discontinuity"));
        view.Members.Excluded.Select(e => (e.Symbol, e.Reason)).ShouldContain(("F", "no_bars"));
        view.Members.Counted.ShouldBe(3);
        view.Hold.Number!.Value.ShouldBe(plain.Hold.Number!.Value, 1e-9);
    }

    [Fact]
    public async Task One_daily_bar_read_per_member_plus_one_for_the_index()
    {
        var (basket, _, _, _, _) = Seeded();

        await OkAsync(basket);

        _world.DailyReads.Count.ShouldBe(4);
        _world.DailyReads.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task The_second_call_reads_no_bars_and_returns_the_same_bytes_and_a_new_stored_bar_changes_the_key()
    {
        var (basket, _, _, _, index) = Seeded();
        var first = await OkAsync(basket);
        var reads = _world.DailyReads.Count;

        var second = await OkAsync(basket);

        _world.DailyReads.Count.ShouldBe(reads);
        JsonSerializer.Serialize(second).ShouldBe(JsonSerializer.Serialize(first));

        _world.AddBar(index, ProfileWorld.Sessions[^1].AddDays(3), 1101);
        await OkAsync(basket);

        _world.DailyReads.Count.ShouldBeGreaterThan(reads);
    }

    [Fact]
    public async Task Another_window_is_another_key_and_a_membership_change_is_too()
    {
        var (basket, _, _, _, _) = Seeded();
        await OkAsync(basket);
        var reads = _world.DailyReads.Count;

        await OkAsync(basket, to: ProfileWorld.Sessions[^2].ToString("yyyy-MM-dd"));

        _world.DailyReads.Count.ShouldBeGreaterThan(reads);
    }

    [Fact]
    public async Task Without_from_and_to_the_window_is_the_last_sessions_ending_at_the_newest_bar_and_says_so()
    {
        var (basket, _, _, _, _) = Seeded();

        var result = await _world.Profile().HandleAsync(new GetBasketProfileQuery(basket.ToString("D"), null, null, null, null), Ct);

        var view = result.Value;
        view.Window.Source.ShouldBe("default");
        view.Window.FromSource.ShouldBe("sessions");
        view.Window.ToSource.ShouldBe("newest_bar");
        view.Window.From.ShouldBe(ProfileWorld.Sessions[0]);
        view.Window.To.ShouldBe(ProfileWorld.Sessions[^1]);
    }

    [Fact]
    public async Task An_archived_or_unknown_basket_is_404_reports_not_found()
    {
        var (_, a, _, _, _) = Seeded();
        var archived = _world.Basket("Old", ProfileWorld.Now, a);

        var gone = await RefusedAsync(archived.ToString("D"));
        var unknown = await RefusedAsync(Guid.NewGuid().ToString("D"));

        gone.Code.ShouldBe("reports.not_found");
        gone.Kind.ShouldBe(ErrorKind.NotFound);
        unknown.Code.ShouldBe("reports.not_found");
    }

    [Theory]
    [InlineData("not-a-guid", null, null)]
    [InlineData(null, "2026-10-16", "2026-09-14")]
    [InlineData(null, "2026-10-01", "2026-10-09")]
    [InlineData(null, "yesterday", "2026-10-09")]
    public async Task A_malformed_id_a_reversed_or_short_window_or_a_bad_date_is_422_reports_invalid(string? id, string? from, string? to)
    {
        var (basket, _, _, _, _) = Seeded();

        var error = await RefusedAsync(id ?? basket.ToString("D"), from, to);

        error.Code.ShouldBe("reports.invalid");
        error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task More_members_than_the_cap_is_422_before_any_bar_is_read()
    {
        var (basket, _, _, _, _) = Seeded();
        _world.Settings.MaxInstruments = 2;

        var error = await RefusedAsync(basket.ToString("D"));

        error.Code.ShouldBe("reports.invalid");
        _world.DailyReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Capacity_is_the_system_share_of_the_adv20_and_the_view_prints_the_share_used()
    {
        var k = Enumerable.Range(0, 3).Select(i => _world.Instrument($"K{i}", _ => 100)).ToArray();
        _world.IndexIsBenchmark(_world.Instrument("NIFTY", i => 1000 + i));
        var basket = _world.Basket("Flat", null, k);

        var view = await OkAsync(basket);

        // close 100 x volume 1000 on every session: ADV20 100,000 per member, 0.5 % of it 500, three members 1,500
        view.Capacity.CapacityPct.ShouldBe(0.005, 1e-12);
        view.Capacity.BasketCapacity.Number!.Value.ShouldBe(1500, 1e-6);
        view.Evidence.Limits.ShouldContain(l => l.Contains("0.5%", StringComparison.Ordinal) && l.Contains("CapacityDailyShare", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_portfolio_profile_misses_the_cache_when_a_holding_changes()
    {
        var (_, a, b, c, _) = Seeded();
        var id = Guid.NewGuid();
        var holdings = _world.PortfolioBasket(id, "Dhan");
        holdings.AddRange([(a, 10), (b, 5), (c, 7)]);
        await OkAsync(id);
        var reads = _world.DailyReads.Count;
        await OkAsync(id);
        _world.DailyReads.Count.ShouldBe(reads);

        holdings[0] = (a, 12);
        await OkAsync(id);

        _world.DailyReads.Count.ShouldBeGreaterThan(reads);
    }

    [Fact]
    public async Task A_member_added_and_removed_inside_the_window_has_its_flags_named_unknown_not_false()
    {
        var (_, a, b, c, _) = Seeded();
        var basket = _world.Basket("Churn", null, a, b, c);
        var brief = _world.Instrument("BRIEF", i => 100 + i);
        _world.Stay(basket, brief, ProfileWorld.At(ProfileWorld.Sessions[3]), ProfileWorld.At(ProfileWorld.Sessions[8]));

        var view = await OkAsync(basket);

        view.MembershipBasis.ShouldBe("dated");
        view.Evidence.Limits.ShouldContain(l => l.Contains("flags are not known for BRIEF", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changes_before_the_window_leave_a_single_snapshot()
    {
        var (_, a, b, c, _) = Seeded();
        var basket = _world.Basket("Early", null, a, b);
        _world.Stay(basket, c, new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero));

        var view = await OkAsync(basket);

        view.MembershipBasis.ShouldBe("single_snapshot");
    }

    [Fact]
    public async Task A_window_of_fewer_than_20_trading_days_is_422_by_the_calendar_not_the_weekdays()
    {
        var (basket, _, _, _, _) = Seeded();
        var holidays = Enumerable.Range(0, 8).Select(i => XDrishti.Domain.Market.Calendar.TradingDay.Holiday(ProfileWorld.Sessions[i], XDrishti.Domain.Market.Calendar.CalendarSource.Manual, XDrishti.Domain.Market.Calendar.CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)).ToList();
        _world.Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<XDrishti.Domain.Market.Calendar.TradingCalendar>(new XDrishti.Domain.Market.Calendar.TradingCalendar(holidays)));

        var error = await RefusedAsync(basket.ToString("D"));

        error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task The_strategy_trades_of_the_basket_are_split_at_the_median_exit_date_through_the_loader()
    {
        var (basket, _, _, _, _) = Seeded();
        _world.Runs.AddRun('a', "ORB", "basket:" + basket.ToString("D"));
        _world.Runs.AddTrades('a', [.. Enumerable.Repeat(1.0, 40), .. Enumerable.Repeat(-1.0, 40)], "ORB", baskets: [basket.ToString("D")]);

        var view = await OkAsync(basket);

        view.HoldOut.Trades.ShouldBe(80);
        var cell = view.HoldOut.Cells.Single();
        cell.Strategy.ShouldBe("ORB");
        cell.Verdict.ShouldBe("disagrees");
        (cell.FirstN, cell.SecondN).ShouldBe((40, 40));
    }

    [Fact]
    public async Task Without_trades_the_hold_out_is_empty_and_a_cell_under_30_per_half_is_insufficient()
    {
        var (basket, _, _, _, _) = Seeded();
        var none = await OkAsync(basket);
        _world.Runs.AddRun('a', "ORB", "basket:" + basket.ToString("D"));
        _world.Runs.AddTrades('a', Enumerable.Repeat(1.0, 40), "ORB", baskets: [basket.ToString("D")]);

        var thin = await OkAsync(basket);

        none.HoldOut.Status.State.ShouldBe("empty");
        thin.HoldOut.Cells.Single().Verdict.ShouldBe("insufficient");
        thin.HoldOut.Cells.Single().FirstExpectancy.Number.ShouldBeNull();
    }

    [Fact]
    public async Task Every_response_carries_the_evidence_block_and_the_words_alpha_excess_and_outperformance_never_label_the_difference()
    {
        var (basket, _, _, _, _) = Seeded();

        var view = await OkAsync(basket);

        view.Evidence.Basis.ShouldBe("equal weight, daily rebalanced, price return, dividends not included");
        view.Evidence.Limits.ShouldNotBeEmpty();
        view.Evidence.N.ShouldBe(3);
        view.Evidence.Range.From.ShouldBe(view.Window.From);
        var text = JsonSerializer.Serialize(view.IndexDifference);
        text.ShouldNotContain("alpha", Case.Insensitive);
        text.ShouldNotContain("excess", Case.Insensitive);
        text.ShouldNotContain("outperform", Case.Insensitive);
    }

    [Fact]
    public async Task A_figure_carries_a_number_if_and_only_if_its_state_is_a_value_over_the_whole_view_and_the_same_data_gives_identical_bytes()
    {
        var (basket, a, b, c, _) = Seeded();
        var states = new HashSet<string>();

        foreach (var id in new[] { basket, _world.Basket("Four", null, a, b, c, _world.Instrument("D", i => 90 + i)), _world.Basket("Empty", null, _world.Instrument("X")) })
        {
            var view = await OkAsync(id);
            foreach (var metric in Metrics(view))
            {
                (metric.Number is not null).ShouldBe(metric.State is "value" or "provisional", $"{metric.State} {metric.Reason}");
                states.Add(metric.State);
            }
        }

        states.ShouldContain("value");
        states.ShouldContain("insufficient");
        states.ShouldContain("empty");
        states.ShouldContain("unavailable");
        var again = await OkAsync(basket);
        JsonSerializer.Serialize(again).ShouldBe(JsonSerializer.Serialize(await OkAsync(basket)));
    }

    private static IEnumerable<MetricView> Metrics(object? node)
    {
        switch (node)
        {
            case null or string:
                yield break;
            case MetricView m:
                yield return m;
                yield break;
            case IEnumerable list:
                foreach (var item in list)
                {
                    foreach (var m in Metrics(item))
                    {
                        yield return m;
                    }
                }

                yield break;
        }

        if (node.GetType().Namespace != typeof(MetricView).Namespace)
        {
            yield break;
        }

        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            foreach (var m in Metrics(p.GetValue(node)))
            {
                yield return m;
            }
        }
    }
}
