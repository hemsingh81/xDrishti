using System.Net;
using XDrishti.Api.IntegrationTests.BrokerHistory;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Api.IntegrationTests.Dividends;

/// <summary>
/// Dividends the owner enters, over HTTP on the real database and a fake Dhan (P2-14, ADR 0026): adding, listing, voiding, the duplicate confirmation, the audit trail without amounts, and the income figures of the summary, the rows and the groups.
/// The account holds EXMPL (30 shares at 100, the lot dated Fri 9 Oct 2026, last price 120); the clock is Monday 2026-10-12 10:00 India time.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DividendsApiTests(ApiFactory factory) : BrokerHistoryTestBase(factory)
{
    private async Task<HoldingsSummary> SummaryAsync(string query = "") => await Client.SummaryAsync(query, Ct);

    private async Task<HoldingGroups> GroupsAsync(string query)
    {
        using var response = await Client.GetAsync("/api/holdings/groups" + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<HoldingGroups>(Ct);
    }

    [Fact]
    public async Task A_dividend_is_added_listed_voided_and_counted_in_the_figures_of_the_row_the_summary_and_the_groups()
    {
        var held = await HeldAsync();
        var before = await SummaryAsync();

        var added = await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 300.5, perShare = 10.0167, note = "final" }, Ct);
        var unallocated = await Client.AddDividendAsync(new { accountId = held.Account.Id, payDate = "2026-09-01", amount = 100 }, Ct);
        var row = await RowAsync();
        var summary = await SummaryAsync();
        var accountSummary = await SummaryAsync($"?accountId={held.Account.Id}");
        var buckets = await GroupsAsync("?by=bucket");

        (added.AccountId, added.HoldingId, added.Symbol, added.Isin, added.Amount, added.PerShare, added.Note, added.Source, added.CreatedBy).ShouldBe(
            (held.Account.Id, held.HoldingId, "EXMPL", TradesApi.Isin, 300.5m, 10.0167m, "final", "manual", ApiFactory.OwnerEmail));
        (unallocated.HoldingId, unallocated.Symbol).ShouldBe((null, null));
        before.Income.ShouldBeNull();
        var income = row.Income.ShouldNotBeNull();
        (income.Dividends, income.Charges, income.Unallocated, income.TotalReturnPct, income.NetTotalReturnPct).ShouldBe((300.5m, 0m, null, 30.0167m, 30.0167m)); // (600 + 300.50) over 3,000
        (income.DividendsFrom, income.ChargesFrom, income.CoverageFrom).ShouldBe((Oct(1), null, Oct(1)));
        (summary.Income!.Dividends, summary.Income.Unallocated, accountSummary.Income!.Dividends, accountSummary.Income.Unallocated).ShouldBe((400.5m, 100m, 400.5m, 100m));
        buckets.Groups.Select(g => (g.Key, g.Income!.Dividends)).ShouldBe([(buckets.Groups[0].Key, 300.5m), ("~unallocated", 100m)]);
        buckets.Groups.Sum(g => g.Income!.Dividends).ShouldBe(buckets.Total.Income!.Dividends);

        var gross = summary with { Income = null };
        gross.ShouldBe(before with { Income = null }); // value, profit, XIRR, benchmark and alpha do not move with a dividend

        var voided = await Client.VoidDividendAsync(added.Id, Ct);
        var page = await Client.HoldingDividendsAsync(held.HoldingId, "", Ct);

        (voided.Voided, voided.VoidedBy).ShouldBe((true, ApiFactory.OwnerEmail));
        page.Items.ShouldHaveSingleItem().Voided.ShouldBeTrue();
        (await RowAsync()).Income.ShouldBeNull();
        (await SummaryAsync()).Income!.Dividends.ShouldBe(100m);
    }

    [Fact]
    public async Task The_dividends_of_a_holding_and_of_an_account_are_listed_newest_first_and_paged()
    {
        var held = await HeldAsync();
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-01-01", amount = 1 }, Ct);
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-09-01", amount = 3 }, Ct);
        await Client.AddDividendAsync(new { accountId = held.Account.Id, payDate = "2026-05-01", amount = 2 }, Ct);

        var holding = await Client.HoldingDividendsAsync(held.HoldingId, "", Ct);
        var account = await Client.AccountDividendsAsync(held.Account.Id, "?pageSize=2&page=1", Ct);
        var second = await Client.AccountDividendsAsync(held.Account.Id, "?pageSize=2&page=2", Ct);

        holding.Items.Select(d => d.Amount).ShouldBe([3m, 1m]);
        account.Items.Select(d => d.Amount).ShouldBe([3m, 2m]);
        account.Total.ShouldBe(3);
        second.Items.Select(d => d.Amount).ShouldBe([1m]);
        second.Items[0].HoldingId.ShouldBe(held.HoldingId);
    }

    [Fact]
    public async Task The_same_dividend_twice_is_a_409_until_it_is_confirmed()
    {
        var held = await HeldAsync();
        var body = new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 50 };
        await Client.AddDividendAsync(body, Ct);

        using var asked = await Client.AddDividendRawAsync(body, Ct);
        using var confirmed = await Client.AddDividendRawAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 50, confirm = true }, Ct);

        asked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await asked.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.possible_duplicate");
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.HoldingDividendsAsync(held.HoldingId, "", Ct)).Total.ShouldBe(2);
        (await RowAsync()).Income!.Dividends.ShouldBe(100m);
    }

    [Fact]
    public async Task Bad_values_are_a_400_with_the_fields_and_an_unknown_account_or_holding_is_not_found()
    {
        var held = await HeldAsync();

        using var bad = await Client.AddDividendRawAsync(new { holdingId = held.HoldingId, payDate = "2027-01-01", amount = 0, perShare = -1, note = new string('x', 201) }, Ct);
        using var none = await Client.AddDividendRawAsync(new { }, Ct);
        using var noAccount = await Client.AddDividendRawAsync(new { accountId = Guid.NewGuid(), payDate = "2026-10-01", amount = 5 }, Ct);
        using var noHolding = await Client.AddDividendRawAsync(new { holdingId = Guid.NewGuid(), payDate = "2026-10-01", amount = 5 }, Ct);
        using var noVoid = await Client.PostAsync($"/api/portfolio/dividends/{Guid.NewGuid()}/void", Ct);

        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["payDate", "amount", "perShare", "note"], ignoreOrder: true);
        none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noAccount.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        noHolding.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noVoid.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadCodeAsync(noVoid)).ShouldBe("portfolio.not_found");
        (await CountAsync("SELECT count(*) FROM portfolio.dividends")).ShouldBe(0);
    }

    [Fact]
    public async Task An_account_without_the_portfolio_role_cannot_have_dividends()
    {
        var account = await UnconnectedAccountAsync(portfolioRole: false);

        using var response = await Client.AddDividendRawAsync(new { accountId = account.Id, payDate = "2026-10-01", amount = 5 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("portfolio.role_off");
    }

    [Fact]
    public async Task The_audit_trail_names_the_dividend_and_never_its_amount()
    {
        var held = await HeldAsync();
        var added = await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 98765.43 }, Ct);
        await Client.VoidDividendAsync(added.Id, Ct);
        await Client.VoidDividendAsync(added.Id, Ct);

        var details = await DetailsAsync("DividendAdded", "DividendVoided");

        details.Order().ShouldBe([$"Dividend {added.Id} added", $"Dividend {added.Id} voided"]);
        details.ShouldAllBe(d => !d.Contains("98765", StringComparison.Ordinal) && !d.Contains("EXMPL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trade_charges_and_the_ledgers_costs_are_counted_once_each_and_the_ledgers_settlements_not_at_all()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger(); // funds in, a purchase settlement of 900, a dividend credit of 1,500, DP charges of 15.93, a withdrawal
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-07", amount = 1500 }, Ct);
        using var trade = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "buy", tradeDate = "2026-10-05", quantity = 1, price = 90, charges = 12.5 }, Ct);
        var tradeId = (await trade.ReadAsync<XDrishti.Application.Portfolio.Trades.TradeView>(Ct)).Id;
        await SyncAndWaitAsync(held.Account.Id);

        var row = await RowAsync();
        var summary = await SummaryAsync();
        using var voided = await Client.PostAsync($"/api/portfolio/trades/{tradeId}/void", Ct);
        var afterVoid = await SummaryAsync();

        row.Income!.Charges.ShouldBe(12.5m); // the holding's trade charge only: the ledger's cost belongs to the account
        (summary.Income!.Charges, summary.Income.Dividends, summary.Income.Unallocated).ShouldBe((28.43m, 1500m, 0m)); // 12.50 of the trade + 15.93 of the ledger
        (summary.Income.ChargesFrom, summary.Income.DividendsFrom).ShouldBe((Oct(5), Oct(7)));
        afterVoid.Income!.Charges.ShouldBe(15.93m);
    }

    [Fact]
    public async Task A_ledger_that_repeats_the_costs_of_trades_does_not_count_them_twice_and_the_ledgers_own_costs_are_given_apart()
    {
        var held = await HeldOnTheNseAsync();
        DhanHasTheBaseTrades();
        DhanHasALedger(); // DP charges of 15.93 among the lines
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(6), "V6", "JOURNAL", "BROKERAGE 06-10-2026 EXMPL", 12.5m, 0m, 25000m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(6), "V7", "JOURNAL", "STT 06-10-2026 EXMPL", 0.5m, 0m, 25000m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(6), "V8", "BILL", "BILL FOR 06-OCT-2026 BROKERAGE STT GST STAMP DUTY", 20m, 0m, 25000m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(9), "V9", "JOURNAL", "ANNUAL MAINTENANCE CHARGES FY 2026", 354m, 0m, 25000m));
        Dhan.Ledger.Add(new FakeBrokers.FakeLedgerLine(D(9), "V10", "JOURNAL", "DPC Interest : 01/09/2026 to 30/09/2026", 8.2m, 0m, 25000m));
        using var trade = await Client.PostJsonAsync("/api/portfolio/trades", new { holdingId = held.HoldingId, side = "buy", tradeDate = "2026-10-05", quantity = 1, price = 90, charges = 13 }, Ct);
        trade.StatusCode.ShouldBe(HttpStatusCode.OK, await trade.Content.ReadAsStringAsync(Ct));
        await SyncAndWaitAsync(held.Account.Id);

        var row = await RowAsync();
        var summary = await SummaryAsync();
        var groups = await GroupsAsync("?by=account");

        row.Income!.Charges.ShouldBe(13m); // the trade's own charges, once
        (summary.Income!.Charges, summary.Income.LedgerCharges).ShouldBe((13m + 15.93m + 354m + 8.2m, 15.93m + 354m + 8.2m)); // DP charges, annual maintenance and delayed-payment interest; not brokerage, STT or the bill
        (groups.Total.Income!.Charges, groups.Total.Income.LedgerCharges).ShouldBe((summary.Income.Charges, summary.Income.LedgerCharges));
        (row.Income.LedgerCharges, row.Income.TradeChargesFrom).ShouldBe((null, Oct(5)));
        (summary.Income.TradeChargesFrom, summary.Income.LedgerChargesFrom, summary.Income.ChargesFrom).ShouldBe((Oct(5), Oct(5), Oct(5)));
    }

    [Fact]
    public async Task A_bucket_leaves_out_what_belongs_to_the_account_and_an_account_keeps_it()
    {
        var held = await HeldAsync();
        await Client.AddDividendAsync(new { accountId = held.Account.Id, payDate = "2026-10-01", amount = 10 }, Ct);
        await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = "2026-10-01", amount = 20 }, Ct);
        var bucket = (await RowAsync()).BucketId;

        var byBucket = await SummaryAsync($"?bucketId={bucket}");
        var byAccount = await SummaryAsync($"?accountId={held.Account.Id}");

        (byBucket.Income!.Dividends, byBucket.Income.Unallocated).ShouldBe((20m, null));
        (byAccount.Income!.Dividends, byAccount.Income.Unallocated).ShouldBe((30m, 10m));
    }

    [Fact]
    public async Task The_income_adds_a_fixed_number_of_statements_whatever_the_number_of_dividends_and_trades()
    {
        var held = await HeldAsync();
        for (var i = 1; i <= 12; i++)
        {
            await Client.AddDividendAsync(new { holdingId = held.HoldingId, payDate = $"2026-09-{i:00}", amount = i }, Ct);
        }

        var statements = await Client.StatementsDuringAsync(async () => (await SummaryAsync()).Income!.Dividends.ShouldBe(78m));
        var groups = await Client.StatementsDuringAsync(async () => (await GroupsAsync("?by=account")).Total.Income!.Dividends.ShouldBe(78m));

        foreach (var table in new[] { "portfolio.dividends", "portfolio.transactions", "portfolio.ledger_entries" })
        {
            statements.Count(s => s.Contains(table, StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(2, table); // a read of the table (the ledger: costs and the first date), never one per row
            groups.Count(s => s.Contains(table, StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(2, table);
        }
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response) => (await response.ReadProblemAsync(Ct)).Code!;

    private async Task<List<string>> DetailsAsync(params string[] events)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT detail FROM accounts.account_events WHERE event = ANY(@events)", connection);
        command.Parameters.AddWithValue("events", events);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var details = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            details.Add(reader.GetString(0));
        }

        return details;
    }
}
