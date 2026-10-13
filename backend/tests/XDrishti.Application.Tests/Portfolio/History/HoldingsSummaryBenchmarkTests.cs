using XDrishti.Application.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>Benchmark XIRR, alpha and state on the holdings summary (acceptance criterion 7 of P2-07; clock: Saturday 2026-10-10, India).</summary>
public sealed class HoldingsSummaryBenchmarkTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly TwoYearsAgo = new(2024, 10, 10);

    private static readonly DateOnly LastFriday = new(2026, 10, 9);

    private async Task<Guid> ImportedAsync(DateOnly date)
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.Add(Row("AAA", 100, 50m, 60m, "1", "INE000A00001"));
        await _fx.ImportAllAsync(account.Id, date);
        return account.Id;
    }

    private async Task<HoldingsSummary> SummaryAsync(Guid? account = null) => Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(account, null), CancellationToken.None));

    private Guid IndexWithCloses(params (DateOnly Date, double Close)[] closes)
    {
        var index = _fx.Benchmarks.Add("NSE:NIFTY", "Nifty 50", tracked: true, hasBars: closes.Length > 0);
        _fx.Benchmarks.Default = index.Id;
        _fx.Prices.Set(index.Id, closes);
        return index.Id;
    }

    [Fact]
    public async Task The_benchmark_xirr_alpha_and_state_come_with_the_summary()
    {
        // 5,000 into the index (1,000) two years ago is 6,050 at the last close (1,210): exactly 10 % a year; the holding made 20 % in the same two years (9.5445 % a year).
        await ImportedAsync(TwoYearsAgo);
        IndexWithCloses((TwoYearsAgo, 1000.0), (LastFriday, 1210.0));

        var summary = await SummaryAsync();

        summary.BenchmarkState.ShouldBe("ok");
        summary.BenchmarkXirr.ShouldNotBeNull().ShouldBe(10m, 0.001m);
        summary.Alpha.ShouldNotBeNull().ShouldBe(-0.4555m, 0.001m);
        summary.Returns.XirrState.ShouldBe("ok");
        (summary.Returns.Invested, summary.Returns.Value).ShouldBe((5000m, 6000m));
    }

    [Fact]
    public async Task Only_the_benchmark_index_is_read_for_the_summary()
    {
        await ImportedAsync(TwoYearsAgo);
        var index = IndexWithCloses((TwoYearsAgo, 1000.0), (LastFriday, 1210.0));

        await SummaryAsync();

        _fx.Prices.Calls.ShouldBe(1);
        _fx.Prices.Last.ShouldNotBeNull().Ids.ShouldBe([index]);
        _fx.Prices.Last!.Value.To.ShouldBe(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public async Task Without_a_benchmark_index_the_state_is_unavailable_and_the_figures_are_empty_while_the_rest_is_unchanged()
    {
        await ImportedAsync(TwoYearsAgo);

        var summary = await SummaryAsync();

        (summary.BenchmarkState, summary.BenchmarkXirr, summary.Alpha).ShouldBe(("unavailable", null, null));
        (summary.Returns.Invested, summary.Returns.Value, summary.Returns.XirrState, summary.OpenCount).ShouldBe((5000m, 6000m, "ok", 1));
    }

    [Fact]
    public async Task An_index_without_bars_is_unavailable_never_zero()
    {
        await ImportedAsync(TwoYearsAgo);
        IndexWithCloses();

        var summary = await SummaryAsync();

        (summary.BenchmarkState, summary.BenchmarkXirr, summary.Alpha).ShouldBe(("unavailable", null, null));
    }

    [Fact]
    public async Task An_index_that_starts_after_the_first_flow_is_unavailable()
    {
        await ImportedAsync(TwoYearsAgo);
        IndexWithCloses((new DateOnly(2025, 1, 2), 1000.0), (LastFriday, 1210.0));

        (await SummaryAsync()).BenchmarkState.ShouldBe("unavailable");
    }

    [Fact]
    public async Task A_span_under_thirty_days_is_insufficient_like_the_xirr()
    {
        await ImportedAsync(new DateOnly(2026, 10, 1));
        IndexWithCloses((new DateOnly(2026, 10, 1), 1000.0), (LastFriday, 1010.0));

        var summary = await SummaryAsync();

        (summary.BenchmarkState, summary.BenchmarkXirr, summary.Alpha, summary.Returns.XirrState).ShouldBe(("insufficient", null, null, "insufficient"));
    }

    [Fact]
    public async Task A_scope_without_holdings_is_insufficient_and_reads_no_prices()
    {
        var account = await _fx.ConnectedAsync();
        IndexWithCloses((TwoYearsAgo, 1000.0));

        var summary = await SummaryAsync(account.Id);

        summary.BenchmarkState.ShouldBe("insufficient");
        _fx.Prices.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task On_a_weekend_the_prices_and_the_index_are_both_as_of_the_last_close()
    {
        await ImportedAsync(TwoYearsAgo);
        IndexWithCloses((TwoYearsAgo, 1000.0), (LastFriday, 1210.0));

        var summary = await SummaryAsync();

        summary.BenchmarkAsOf.ShouldBe(LastFriday);
        summary.AlphaBasis.ShouldBe("last_close"); // the prices are dated Saturday: no session lies between the index close and them
    }

    [Fact]
    public async Task On_a_live_day_the_alpha_says_the_portfolio_is_live_and_the_index_is_at_its_previous_close()
    {
        // Monday 12 Oct, 10:00: the holdings are priced today, the index has its Friday bar only. The index's move of the day is not in the alpha, and the response says so.
        _fx.Clock.SetUtcNow(HistoryFixture.MondayMorning);
        await ImportedAsync(TwoYearsAgo);
        IndexWithCloses((TwoYearsAgo, 1000.0), (LastFriday, 1210.0));

        var summary = await SummaryAsync();

        summary.BenchmarkState.ShouldBe("ok");
        summary.BenchmarkAsOf.ShouldBe(LastFriday);
        summary.AlphaBasis.ShouldBe("live_vs_last_close");
        summary.Alpha.ShouldNotBeNull();
        summary.ValuationDate.ShouldBe(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public async Task Without_an_alpha_there_is_no_basis_and_without_an_index_no_as_of()
    {
        await ImportedAsync(new DateOnly(2026, 10, 1));
        IndexWithCloses((new DateOnly(2026, 10, 1), 1000.0), (LastFriday, 1010.0));

        var shortSpan = await SummaryAsync();

        (shortSpan.Alpha, shortSpan.AlphaBasis).ShouldBe((null, null));
        shortSpan.BenchmarkAsOf.ShouldBe(LastFriday); // the index was used; the span is just too short for a rate
    }
}
