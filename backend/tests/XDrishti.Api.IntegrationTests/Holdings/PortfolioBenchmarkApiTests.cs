using System.Net;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The benchmark of the portfolio over HTTP on the real database (P2-07, ADR 0020 §2): the same cash flows in the index in the history and the summary, the setting (default, choice, validation, audit, cache), tracked and
/// untracked indexes and the two track actions (idempotent, no job started). Clock: Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioBenchmarkApiTests(ApiFactory factory) : PortfolioHistoryTestBase(factory)
{
    private static readonly (DateOnly, double)[] IndexCloses = [(Oct(5), 1000), (Oct(6), 1100), (Oct(7), 1200), (Oct(8), 900), (Oct(9), 1000)];

    [Fact]
    public async Task The_history_puts_the_same_cash_flows_into_the_index()
    {
        var (_, alpha, _) = await TwoHoldingsAsync();
        await AddSplitAsync(alpha, "2026-10-07", 1, 2);
        await SetLotDateAsync("BETA", "2026-10-07");
        await IndexAsync("NSE:NIFTY", "Nifty 50", IndexCloses);

        var history = await HistoryAsync();

        // 5,000 buys 5 units on the 5th, 2,000 more buys 1.6667 units at 1,200 on the 7th; the index has no quote for today, so its lines end at the last close (the 9th) and today's live point has none
        history.Points.Select(p => p.BenchmarkValue).ShouldBe(new decimal?[] { 5000m, 5500m, 8000m, 6000m, 6666.6667m, null });
        history.Points.Select(p => p.BenchmarkNav).ShouldBe(new decimal?[] { 100m, 110m, 120m, 90m, 100m, null });
        history.Benchmark.ShouldBe(new HistoryBenchmark("ok", history.Benchmark.InstrumentId, "NSE:NIFTY", "Nifty 50", false, true, null, Oct(9)));
        history.BenchmarkMaxDrawdown.ShouldNotBeNull().Pct.ShouldBe(25m);
        // units are issued at the NAV of the day BETA is bought: peak NAV 120 on the 7th, low 13,800 / 143.33 units = 96.28 on the 8th
        var drawdown = history.MaxDrawdown.ShouldNotBeNull();
        (drawdown.From, drawdown.To).ShouldBe((Oct(7), Oct(8)));
        drawdown.Pct.ShouldBe(19.7674m, 0.001m);
    }

    [Fact]
    public async Task An_untracked_index_without_bars_is_unavailable_and_tracking_it_is_idempotent_and_starts_no_job()
    {
        await TwoHoldingsAsync();
        await IndexAsync("NSE:NIFTY", "Nifty 50");

        var before = await HistoryAsync();
        var first = await TrackAsync("/api/portfolio/history/track-benchmark");
        var second = await TrackAsync("/api/portfolio/history/track-benchmark");
        var after = await HistoryAsync();

        before.Benchmark.State.ShouldBe("unavailable");
        (before.Benchmark.Tracked, before.Benchmark.HasBars).ShouldBe((false, false));
        before.Benchmark.Reason.ShouldNotBeNull().ShouldContain("Track it");
        before.Points.ShouldAllBe(p => p.BenchmarkValue == null && p.BenchmarkNav == null);
        before.Points.Count.ShouldBe(6); // value and invested do not wait for the benchmark
        (first.Requested, first.Added, first.AlreadyTracked, first.TotalTracked).ShouldBe((1, 1, 0, 1));
        first.Hint.ShouldContain("Update data");
        (second.Added, second.AlreadyTracked, second.TotalTracked).ShouldBe((0, 1, 1));
        (after.Benchmark.Tracked, after.Benchmark.State).ShouldBe((true, "unavailable"));
        after.Benchmark.Reason.ShouldNotBeNull().ShouldContain("Update data");
        (await CountAsync("SELECT count(*) FROM market.data_jobs")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'InstrumentsTracked'")).ShouldBe(1);
    }

    [Fact]
    public async Task Without_the_index_in_the_master_the_benchmark_track_action_is_404()
    {
        await TwoHoldingsAsync();

        using var response = await Client.PostAsync("/api/portfolio/history/track-benchmark", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.not_found");
        (await HistoryAsync()).Benchmark.ShouldBe(new HistoryBenchmark("unavailable", null, null, null, false, false, "The benchmark index is not in the instrument master yet. Refresh the instrument list.", null));
    }

    [Fact]
    public async Task Track_my_holdings_tracks_the_mapped_open_holdings_once_and_counts_the_others()
    {
        var (one, _, _) = await TwoHoldingsAsync(bseOnly: true);

        var first = await TrackAsync("/api/portfolio/history/track-holdings");
        var second = await TrackAsync("/api/portfolio/history/track-holdings");
        var scoped = await TrackAsync($"/api/portfolio/history/track-holdings?accountId={one.Id}");
        var unknown = await TrackAsync($"/api/portfolio/history/track-holdings?accountId={Guid.NewGuid()}");

        first.ShouldBe(new PortfolioTrackResult(2, 2, 0, 1, 0, 2, first.Hint));
        first.Hint.ShouldContain("Update data");
        (second.Added, second.AlreadyTracked, second.NotInMaster).ShouldBe((0, 2, 1));
        (scoped.Added, scoped.AlreadyTracked).ShouldBe((0, 2));
        (unknown.Requested, unknown.Added).ShouldBe((0, 0));
        (await CountAsync("SELECT count(*) FROM market.tracked_instruments WHERE active")).ShouldBe(2);
        (await CountAsync("SELECT count(*) FROM market.data_jobs")).ShouldBe(0);
    }

    [Fact]
    public async Task The_summary_has_the_benchmark_xirr_alpha_and_state_for_the_same_scope()
    {
        // 100 ALPHA bought at 50 on Sat 12 Oct 2024 (5,000) is worth 13,000 at 130 two years later; the index went from 1,000 to 1,210 (the closes of Fri 11 Oct 2024 and Fri 9 Oct 2026): 10 % a year.
        await ShareAsync("ALPHA", "1001", "INE000A01011");
        var account = await ConnectedAccountAsync();
        Dhan.Holdings.Add(Holding("ALPHA", 100, 50m, 130m, "1001", "INE000A01011"));
        await ImportAllAsync(account.Id, "2024-10-12");
        await IndexAsync("NSE:NIFTY", "Nifty 50", (new DateOnly(2024, 10, 11), 1000), (Oct(9), 1210));

        var summary = await Client.SummaryAsync("", Ct);
        var scoped = await Client.SummaryAsync($"?accountId={account.Id}", Ct);

        summary.BenchmarkState.ShouldBe("ok");
        summary.BenchmarkXirr.ShouldNotBeNull().ShouldBe(10m, 0.001m);
        summary.Returns.Xirr.ShouldNotBeNull().ShouldBe(61.4m, 0.5m); // 13,000 / 5,000 over two years
        summary.Alpha.ShouldNotBeNull().ShouldBe(summary.Returns.Xirr.Value - summary.BenchmarkXirr.Value, 0.001m);
        (scoped.BenchmarkState, scoped.BenchmarkXirr, scoped.Alpha).ShouldBe((summary.BenchmarkState, summary.BenchmarkXirr, summary.Alpha));
        // it is Monday morning: the holdings are priced today, the index has its Friday bar only, and the response says so
        (summary.BenchmarkAsOf, summary.AlphaBasis).ShouldBe((Oct(9), "live_vs_last_close"));
        (summary.Returns.Invested, summary.Returns.Value).ShouldBe((5000m, 13000m)); // the existing fields are unchanged
    }

    [Fact]
    public async Task The_summary_says_unavailable_when_the_index_has_no_bars_and_leaves_the_other_figures_alone()
    {
        await TwoHoldingsAsync();
        await IndexAsync("NSE:NIFTY", "Nifty 50");

        var summary = await Client.SummaryAsync("", Ct);

        summary.BenchmarkState.ShouldBe("unavailable");
        (summary.BenchmarkXirr, summary.Alpha).ShouldBe((null, null));
        (summary.Returns.Invested, summary.OpenCount).ShouldBe((7000m, 2));
    }

    [Fact]
    public async Task The_summary_of_a_short_span_is_insufficient_like_the_xirr()
    {
        await TwoHoldingsAsync();
        await IndexAsync("NSE:NIFTY", "Nifty 50", IndexCloses);

        var summary = await Client.SummaryAsync("", Ct);

        (summary.BenchmarkState, summary.BenchmarkXirr, summary.Alpha, summary.Returns.XirrState).ShouldBe(("insufficient", null, null, "insufficient"));
    }

    [Fact]
    public async Task The_benchmark_setting_defaults_to_nifty_50_and_any_listed_index_can_be_chosen_with_an_audit_entry()
    {
        await TwoHoldingsAsync();
        var nifty = await IndexAsync("NSE:NIFTY", "Nifty 50", IndexCloses);
        var bank = await IndexAsync("NSE:NIFTY_BANK", "Nifty Bank", (Oct(5), 2000), (Oct(9), 2100));
        var before = await HistoryAsync();

        var initial = await BenchmarkAsync();
        using var chosen = await Client.PutJsonAsync("/api/portfolio/benchmark", new { instrumentId = bank }, Ct);
        var view = await chosen.ReadAsync<BenchmarkView>(Ct);
        var after = await HistoryAsync(); // the cached history is dropped by the change

        initial.ShouldBe(new BenchmarkView(nifty, "NSE:NIFTY", "Nifty 50", true, false, true));
        before.Benchmark.Symbol.ShouldBe("NSE:NIFTY");
        view.ShouldBe(new BenchmarkView(bank, "NSE:NIFTY_BANK", "Nifty Bank", false, false, true));
        (await BenchmarkAsync()).ShouldBe(view);
        after.Benchmark.Symbol.ShouldBe("NSE:NIFTY_BANK");
        after.Points[0].BenchmarkValue.ShouldBe(after.Points[0].Invested); // the first flow buys at the first close
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'PortfolioBenchmarkChanged' AND actor = '" + ApiFactory.OwnerEmail + "' AND detail = 'Portfolio benchmark changed from NSE:NIFTY to NSE:NIFTY_BANK'")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM platform.app_settings")).ShouldBe(1);
    }

    [Fact]
    public async Task Choosing_the_index_already_in_force_changes_nothing()
    {
        var bank = await IndexAsync("NSE:NIFTY_BANK", "Nifty Bank");
        (await Client.PutJsonAsync("/api/portfolio/benchmark", new { instrumentId = bank }, Ct)).EnsureSuccessStatusCode();

        using var again = await Client.PutJsonAsync("/api/portfolio/benchmark", new { instrumentId = bank }, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'PortfolioBenchmarkChanged'")).ShouldBe(1);
    }

    [Fact]
    public async Task An_instrument_that_is_not_a_listed_index_is_400_portfolio_invalid_and_nothing_is_stored()
    {
        var share = await ShareAsync("ALPHA", "1001", "INE000A01011");
        var retired = await IndexAsync("NSE:OLD_INDEX", "Old index");
        await ExecuteAsync("UPDATE market.instruments SET retired_at = now() WHERE id = @id", ("id", retired));

        foreach (var body in new object[] { new { instrumentId = share }, new { instrumentId = Guid.NewGuid() }, new { instrumentId = retired }, new { instrumentId = Guid.Empty }, new { } })
        {
            using var response = await Client.PutJsonAsync("/api/portfolio/benchmark", body, Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, JsonSerializer.Serialize(body));
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid");
            (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain("instrumentId");
        }

        (await CountAsync("SELECT count(*) FROM platform.app_settings")).ShouldBe(0);
        (await CountAsync("SELECT count(*) FROM accounts.account_events WHERE event = 'PortfolioBenchmarkChanged'")).ShouldBe(0);
    }

    [Fact]
    public async Task Without_any_index_in_the_master_the_setting_is_empty_and_default()
    {
        (await BenchmarkAsync()).ShouldBe(new BenchmarkView(null, null, null, true, false, false));
    }
}
