using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.BrokerHistory;

/// <summary>
/// Why rows were dropped (roadmap ISS-03) over HTTP, through the real clients and mappers and the real database: the status and the run counts carry the reason per code and the place of the first rows - and nothing of the rows' content.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokerDropReasonsApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    [Fact]
    public async Task The_status_and_the_run_say_why_trade_and_ledger_rows_were_dropped_and_where_but_never_what_they_held()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();
        var bad = """{"exchangeTradeId":"ZZTRADE77","transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"ZZSYMBOL77","tradedQuantity":777777,"tradedPrice":0,"exchangeTime":"2026-10-07 10:00:00"}""";
        Dhan.RawTrades.Add((D(7), "~1", bad));
        Dhan.RawTrades.Add((D(7), "~2", """{"transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"ZZSYMBOL77","tradedQuantity":5,"tradedPrice":100,"exchangeTime":"2026-10-07 10:00:00"}"""));
        Dhan.RawTrades.Add((D(7), "~3", "42"));
        Dhan.RawLedger.Add((D(9), """{"narration":"ZZNARRATION 987654.32","voucherdate":"Oct 9, 2026","voucherdesc":"JOURNAL","vouchernumber":"ZZV9","debit":"0.00","credit":"0.00"}"""));
        Dhan.RawLedger.Add((D(9), """{"narration":"ZZNARRATION","voucherdate":"never","voucherdesc":"JOURNAL","vouchernumber":"ZZV10","debit":"1.00","credit":"0.00"}"""));

        var (_, status) = await SyncAndWaitAsync(held.Account.Id);

        var trades = status.Trades.LastRun!.Counts;
        var ledger = status.Ledger.LastRun!.Counts;
        (trades.Stored, trades.Dropped).ShouldBe((3, 3));
        trades.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadPrice] = 1, [BrokerDropReasons.MissingId] = 1, [BrokerDropReasons.UnreadableRow] = 1 });
        trades.DroppedRows.ShouldBe(
        [
            new BrokerDroppedRow(D(5), D(12), 0, 2, BrokerDropReasons.BadPrice),
            new BrokerDroppedRow(D(5), D(12), 0, 3, BrokerDropReasons.MissingId),
            new BrokerDroppedRow(D(5), D(12), 0, 4, BrokerDropReasons.UnreadableRow),
        ]);
        (ledger.Stored, ledger.Dropped).ShouldBe((5, 2));
        ledger.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadDate] = 1, [BrokerDropReasons.ZeroAmount] = 1 });
        ledger.DroppedRows.ShouldBe([new BrokerDroppedRow(D(5), D(12), 0, 5, BrokerDropReasons.ZeroAmount), new BrokerDroppedRow(D(5), D(12), 0, 6, BrokerDropReasons.BadDate)]);
        status.Trades.DroppedByReason.ShouldBe(trades.DroppedByReason);
        status.Trades.DroppedRows.ShouldBe(trades.DroppedRows);
        status.Ledger.DroppedByReason.ShouldBe(ledger.DroppedByReason);

        using var response = await Client.GetAsync($"/api/accounts/{held.Account.Id}/sync/broker-history/status", Ct);
        var wire = await response.Content.ReadAsStringAsync(Ct);
        wire.ShouldContain("\"droppedByReason\":{\"bad_price\":1,\"missing_id\":1,\"unreadable_row\":1}");
        wire.ShouldContain("\"droppedRows\":[{\"windowFrom\":\"2026-10-05\",\"windowTo\":\"2026-10-12\",\"page\":0,\"rowIndex\":2,\"reason\":\"bad_price\"}");
        foreach (var sentinel in new[] { "ZZ", "777777", "987654" })
        {
            wire.ShouldNotContain(sentinel);
        }

        var details = await DetailsAsync();
        details.ShouldContain(d => d.Contains("3 dropped (bad_price×1, missing_id×1, unreadable_row×1)", StringComparison.Ordinal));
        details.ShouldContain(d => d.Contains("2 dropped (bad_date×1, zero_amount×1)", StringComparison.Ordinal));
        details.ShouldAllBe(d => !d.Contains("ZZ", StringComparison.Ordinal) && !d.Contains("777777", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_status_before_any_run_and_a_clean_run_have_no_reasons()
    {
        var held = await HeldOnTheNseAsync();

        var before = await Client.BrokerStatusAsync(held.Account.Id, Ct);
        DhanHasTheBaseTrades();
        var (_, after) = await SyncAndWaitAsync(held.Account.Id);

        before.Trades.DroppedByReason.ShouldBeEmpty();
        before.Trades.DroppedRows.ShouldBeEmpty();
        after.Trades.LastRun!.Counts.Dropped.ShouldBe(0);
        after.Trades.DroppedByReason.ShouldBeEmpty();
        after.Trades.DroppedRows.ShouldBeEmpty();
    }

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private async Task<List<PortfolioNotice>> DroppedItemsAsync()
    {
        using var response = await Client.GetAsync("/api/portfolio/notices", Ct); // a plain read: the items were reconciled when the run ended
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(Ct);
        foreach (var sentinel in new[] { "ZZ", "777777", "987654" })
        {
            text.ShouldNotContain(sentinel);
        }

        return [.. System.Text.Json.JsonSerializer.Deserialize<PortfolioNotices>(text, Web)!.Items.Where(i => i.Kind == "rows_dropped")];
    }

    [Fact]
    public async Task A_run_that_dropped_rows_raises_an_item_when_it_ends_and_a_clean_later_run_resolves_it()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger();
        Dhan.RawTrades.Add((D(7), "~1", """{"transactionType":"BUY","exchangeSegment":"NSE_EQ","tradingSymbol":"ZZSYMBOL77","tradedQuantity":5,"tradedPrice":100,"exchangeTime":"2026-10-07 10:00:00"}"""));
        Dhan.RawLedger.Add((D(9), """{"narration":"ZZNARRATION 987654.32","voucherdate":"Oct 9, 2026","voucherdesc":"JOURNAL","vouchernumber":"ZZV9","debit":"0.00","credit":"0.00"}"""));

        await SyncAndWaitAsync(held.Account.Id);

        var items = await DroppedItemsAsync();
        items.Count.ShouldBe(2);
        var trades = items.Single(i => i.Id.Contains(":trades:", StringComparison.Ordinal));
        var ledger = items.Single(i => i.Id.Contains(":ledger:", StringComparison.Ordinal));
        (trades.Severity, trades.Resolved, trades.AccountId).ShouldBe(("warning", false, held.Account.Id));
        trades.Text.ShouldBe("The latest trades read of Example One dropped 1 row: 1 no trade id. Open the sync line to see where they were.");
        (ledger.Severity, ledger.Resolved).ShouldBe(("info", false));
        ledger.Text.ShouldBe("The latest ledger read of Example One dropped 1 row: 1 zero amount (no money moved, e.g. an opening balance). Open the sync line to see where they were.");
        trades.Actions.ShouldBe([new AttentionAction("Open", "route", "/portfolio?focus=sync")]);

        Dhan.RawTrades.Clear();
        Dhan.RawLedger.Clear();
        await SyncAndWaitAsync(held.Account.Id); // a clean later run of both kinds

        (await DroppedItemsAsync()).ShouldAllBe(i => i.Resolved);
    }

    private async Task<List<string>> DetailsAsync() =>
        await Factory.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.AccountEvents.Where(e => e.Event.ToString() == "BrokerHistorySynced").Select(e => e.Detail!), Ct));
}
