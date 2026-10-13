using XDrishti.Application.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>The day change on the holdings list and summary (ADR 0019 §6): row fields, the sort fields, and the summary equal to the sum of the rows. Clock: Saturday 2026-10-10, India.</summary>
public sealed class HoldingDayChangeQueriesTests
{
    private static readonly DateOnly TwoYearsAgo = new(2024, 10, 10);

    private readonly PortfolioFixture _fx = new();

    private async Task ImportedAsync()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.AddRange([Row("AAA", 100, 50m, 60m, "1", "INE000A00001"), Row("BBB", 10, 200m, 180m, "2", "INE000B00002"), Row("CCC", 5, 1000m, 1500m, "3", "INE000C00003")]);
        await _fx.ImportAllAsync(account.Id, TwoYearsAgo);
    }

    private DateTimeOffset QuoteAt => _fx.Clock.GetUtcNow().AddHours(1);

    private async Task<HoldingPage> ListAsync(string? sort = null, Guid? bucket = null) =>
        Ok(await _fx.List().HandleAsync(new ListHoldingsQuery(new HoldingsRequest(null, bucket, null, null, sort, null, null)), CancellationToken.None));

    private async Task<HoldingsSummary> SummaryAsync() => Ok(await _fx.Summary().HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None));

    [Fact]
    public async Task A_row_with_a_quote_carries_the_previous_close_the_day_change_the_price_time_and_the_source()
    {
        await ImportedAsync();
        _fx.HoldingOf("AAA").ApplyQuote(63m, 60m, QuoteAt);

        var row = (await ListAsync(sort: "symbol")).Items[0];

        (row.PreviousClose, row.DayChange, row.DayChangePct, row.PriceAt, row.PriceSource, row.LastPriceAt).ShouldBe((60m, 300m, 5m, QuoteAt, "quote", QuoteAt));
    }

    [Fact]
    public async Task A_row_without_a_quote_has_no_day_change_and_the_sync_source()
    {
        await ImportedAsync();

        var row = (await ListAsync(sort: "symbol")).Items[0];

        (row.PreviousClose, row.DayChange, row.DayChangePct, row.PriceSource).ShouldBe((null, null, null, "sync"));
        row.PriceAt.ShouldBe(row.LastPriceAt);
    }

    [Fact]
    public async Task The_day_change_sort_fields_order_by_rupees_and_percent_with_rows_without_one_last()
    {
        await ImportedAsync();
        _fx.HoldingOf("AAA").ApplyQuote(63m, 60m, QuoteAt); // +300, +5 %
        _fx.HoldingOf("BBB").ApplyQuote(171m, 180m, QuoteAt); // -90, -5 %
        // CCC: no quote

        (await ListAsync("dayChange")).Items.Select(i => i.Symbol).ShouldBe(["BBB", "AAA", "CCC"]);
        (await ListAsync("-dayChange")).Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB", "CCC"]);
        (await ListAsync("dayChangePct")).Items.Select(i => i.Symbol).ShouldBe(["BBB", "AAA", "CCC"]);
        (await ListAsync("-dayChangePct")).Items.Select(i => i.Symbol).ShouldBe(["AAA", "BBB", "CCC"]);
    }

    [Fact]
    public async Task The_summary_day_change_is_the_sum_of_the_rows_and_counts_the_holdings_left_out()
    {
        await ImportedAsync();
        _fx.HoldingOf("AAA").ApplyQuote(63m, 60m, QuoteAt); // +300 on 6,000
        _fx.HoldingOf("BBB").ApplyQuote(171m, 180m, QuoteAt); // -90 on 1,800
        // CCC has no previous close

        var summary = await SummaryAsync();
        var rows = (await ListAsync()).Items;

        (summary.DayChange, summary.DayChangePct, summary.DayChangeExcluded).ShouldBe((210m, 2.6923m, 1));
        summary.DayChange.ShouldBe(rows.Sum(r => r.DayChange ?? 0m));
        rows.Count(r => r.DayChange is null).ShouldBe(summary.DayChangeExcluded);
    }

    [Fact]
    public async Task The_summary_day_change_is_null_when_no_holding_has_a_previous_close()
    {
        await ImportedAsync();

        var summary = await SummaryAsync();

        (summary.DayChange, summary.DayChangePct, summary.DayChangeExcluded).ShouldBe((null, null, 3));
    }

    [Fact]
    public async Task The_summary_gives_the_oldest_and_the_newest_price_time_of_the_open_holdings()
    {
        await ImportedAsync();
        var synced = _fx.HoldingOf("AAA").LastPriceAt;
        _fx.HoldingOf("BBB").ApplyQuote(171m, 180m, QuoteAt);

        var summary = await SummaryAsync();

        (summary.PricesAsOf.Oldest, summary.PricesAsOf.Newest).ShouldBe((synced, QuoteAt));
    }

    [Fact]
    public async Task A_quote_moves_the_summary_value_and_pnl_by_the_same_amount()
    {
        await ImportedAsync();
        var before = await SummaryAsync();
        _fx.HoldingOf("AAA").ApplyQuote(66m, 60m, QuoteAt); // +6 x 100

        var after = await SummaryAsync();

        (after.Returns.Value - before.Returns.Value, after.Returns.Pnl - before.Returns.Pnl).ShouldBe((600m, 600m));
    }

    [Fact]
    public async Task Without_holdings_the_price_times_are_empty()
    {
        var summary = await SummaryAsync();

        (summary.PricesAsOf.Oldest, summary.PricesAsOf.Newest, summary.DayChange, summary.DayChangeExcluded).ShouldBe((null, null, null, 0));
    }

    [Fact]
    public async Task A_sync_on_the_day_of_a_quote_keeps_the_quoted_price_in_the_list()
    {
        await ImportedAsync();
        var account = _fx.Accounts.Accounts.Single();
        _fx.HoldingOf("AAA").ApplyQuote(63m, 60m, QuoteAt);
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.AddRange([Row("AAA", 100, 50m, 61m, "1", "INE000A00001"), Row("BBB", 10, 200m, 181m, "2", "INE000B00002"), Row("CCC", 5, 1000m, 1500m, "3", "INE000C00003")]);

        Ok(await _fx.SyncAsync(account.Id));

        var rows = (await ListAsync("symbol")).Items;
        (rows[0].LastPrice, rows[0].PriceSource, rows[0].PreviousClose).ShouldBe((63m, "quote", 60m));
        (rows[1].LastPrice, rows[1].PriceSource).ShouldBe((181m, "sync"));
    }
}
