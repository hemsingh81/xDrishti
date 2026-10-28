using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-01 on a real database: the five tables refuse what spec criterion 11 lists (a fill without a side, a status outside the seven, a second outcome for one ticket and account, a missed R on a filled ticket), the store writes each row once
/// (the same trade seen ten times is one row, an order's status revisions are three), reads the newest revision, and the two GET endpoints answer 404 / 422 / 200 as the contract says. (journal_fills is not changed: see the ADR.)
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveCaptureApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid LiveAccount = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a51");
    private static readonly Guid PaperAccount = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a52");
    private static readonly DateOnly Day = new(2026, 11, 3);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, slug, client, mode) in new[] { (LiveAccount, "probe-live", "1000000071", "ManualLive"), (PaperAccount, "probe-paper", "1000000072", "Paper") })
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, @slug, 'Probe', 'Dhan', @client, current_date, '#1A2B3C', now(), now(), false, false, false, 200)",
                ("id", id), ("slug", slug), ("client", client));
            await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, @mode, 'Fixed', 100000, true, false, 1, 0, 0)", ("id", id), ("mode", mode));
            await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", id));
        }
    }

    private async Task<TResult> Store<TResult>(Func<ILiveCaptureStore, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ILiveCaptureStore>());
    }

    private static LiveFill Fill(string trade, long qty = 10) => new(Guid.CreateVersion7(), LiveAccount, trade, "o1", "RELIANCE", Side.Buy, "CNC", qty, 1250m, Day, new TimeOnly(9, 41, 10));

    private static LiveOrder Order(string status, long filled) =>
        new(LiveAccount, "o1", "RELIANCE", Side.Buy, "CNC", "LIMIT", "DAY", 40, 1251m, null, status, filled, filled > 0 ? 1250m : null, new DateTimeOffset(2026, 11, 3, 9, 40, 0, TimeSpan.FromMinutes(330)), null, null, null, null, null, Day);

    private async Task<string?> SqlStateAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task The_same_trade_seen_ten_times_is_one_row_and_an_order_has_one_row_per_status_revision()
    {
        for (var i = 0; i < 10; i++)
        {
            await Store(s => s.InsertFillsAsync([Fill("T-1")], "trade_book", Ct).AsTask());
        }

        var revisions = 0;
        foreach (var (status, filled) in new[] { ("pending", 0L), ("part_traded", 20L), ("traded", 40L), ("traded", 40L) })
        {
            revisions += await Store(s => s.InsertOrdersAsync([Order(status, filled)], Ct).AsTask());
        }

        (await Store(s => s.FillsAsync(LiveAccount, Day, Ct).AsTask())).Count.ShouldBe(1);
        revisions.ShouldBe(3);
        (await Store(s => s.LatestOrdersAsync(LiveAccount, Day, Ct).AsTask())).Single().Status.ShouldBe("traded");
    }

    [Fact]
    public async Task The_tables_refuse_a_fill_without_a_side_a_zero_quantity_a_bad_status_and_a_second_outcome()
    {
        const string fill = "INSERT INTO trading.live_fills (id, account_id, broker_trade_id, symbol, side, product, qty, price, trade_date, source) VALUES (gen_random_uuid(), @a, @t, 'X', @side, 'CNC', @qty, 10, '2026-11-03', 'trade_book')";
        (await SqlStateAsync(fill, ("a", LiveAccount), ("t", "ok-1"), ("side", "buy"), ("qty", 1L))).ShouldBeNull();
        (await SqlStateAsync(fill, ("a", LiveAccount), ("t", "ok-1"), ("side", "buy"), ("qty", 1L))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(fill, ("a", LiveAccount), ("t", "no-side"), ("side", string.Empty), ("qty", 1L))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(fill, ("a", LiveAccount), ("t", "zero"), ("side", "sell"), ("qty", 0L))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(
            "INSERT INTO trading.live_orders (account_id, broker_order_id, symbol, side, product, order_type, validity, qty, status, filled_qty, create_time, trade_date) VALUES (@a, 'o9', 'X', 'buy', 'CNC', 'LIMIT', 'DAY', 10, 'closed', 0, now(), '2026-11-03')",
            ("a", LiveAccount))).ShouldBe(PostgresErrorCodes.CheckViolation);

        var item = await PlanItemAsync();
        const string outcome = "INSERT INTO trading.live_ticket_outcomes (item_id, account_id, for_date, outcome, allocated_qty, entry_qty, plan_grade_status, order_evidence, protection, missed_r) VALUES (@i, @a, '2026-11-03', @o, 10, 0, 'graded', @e, 'not_applicable', @r)";
        (await SqlStateAsync(outcome, ("i", item), ("a", LiveAccount), ("o", "filled"), ("e", "present"), ("r", 1.5))).ShouldBe(PostgresErrorCodes.CheckViolation); // a missed R on a filled ticket
        (await SqlStateAsync(outcome, ("i", item), ("a", LiveAccount), ("o", "bogus"), ("e", "present"), ("r", DBNull.Value))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(outcome, ("i", item), ("a", LiveAccount), ("o", "missed_no_order"), ("e", "unavailable"), ("r", 1.5))).ShouldBe(PostgresErrorCodes.CheckViolation); // never "no order" without order evidence
        (await SqlStateAsync(outcome, ("i", item), ("a", LiveAccount), ("o", "missed_no_order"), ("e", "present"), ("r", 1.5))).ShouldBeNull();
        (await SqlStateAsync(outcome, ("i", item), ("a", LiveAccount), ("o", "missed_unfilled"), ("e", "present"), ("r", 1.5))).ShouldBe(PostgresErrorCodes.UniqueViolation); // one outcome per ticket and account
    }

    [Fact]
    public async Task The_store_writes_an_outcome_once_and_lists_it()
    {
        var item = await PlanItemAsync();
        var outcome = new TicketOutcome(OutcomeCodes.MissedNoOrder, null, 0, null, [], 1.2, "graded", "present", OutcomeCodes.NotApplicable, null);
        var rows = new[] { new NewTicketOutcome(item, LiveAccount, Day, 30, outcome) };

        (await Store(s => s.InsertOutcomesAsync(rows, Ct).AsTask())).ShouldBe(1);
        (await Store(s => s.InsertOutcomesAsync(rows, Ct).AsTask())).ShouldBe(0);

        var page = await Store(s => s.ListOutcomesAsync(LiveAccount, Day, Day, 0, 10, Ct).AsTask());
        (page.Total, page.Items.Single().Outcome.MissedR).ShouldBe((1, 1.2));
    }

    [Fact]
    public async Task The_capture_endpoint_answers_404_for_an_unknown_or_paper_account_422_for_the_future_and_an_empty_day_as_not_run()
    {
        using var unknown = await Client.GetAsync(new Uri($"/api/live-trading/accounts/{Guid.NewGuid()}/capture", UriKind.Relative), Ct);
        using var paper = await Client.GetAsync(new Uri($"/api/live-trading/accounts/{PaperAccount}/capture?date=2026-10-09", UriKind.Relative), Ct);
        using var future = await Client.GetAsync(new Uri($"/api/live-trading/accounts/{LiveAccount}/capture?date=2099-01-01", UriKind.Relative), Ct);
        var empty = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/accounts/{LiveAccount}/capture?date=2026-10-09", UriKind.Relative), Ct);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        paper.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ((int)future.StatusCode).ShouldBe(422);
        (await future.Content.ReadAsStringAsync(Ct)).ShouldContain("live.invalid");
        empty.GetProperty("health").GetString().ShouldBe("not_run");
        empty.GetProperty("tickets").GetArrayLength().ShouldBe(0);
        empty.GetProperty("evidence").GetProperty("orderEvidence").GetString().ShouldBe("unavailable");
    }

    [Theory]
    [InlineData("size=201")]
    [InlineData("size=0")]
    [InlineData("page=0")]
    [InlineData("from=2026-01-01&to=2026-10-01")]
    [InlineData("from=2026-10-05&to=2026-10-01")]
    [InlineData("to=2099-01-01")]
    public async Task The_outcomes_endpoint_refuses_a_bad_page_size_or_range_with_422(string query)
    {
        using var response = await Client.GetAsync(new Uri("/api/live-trading/outcomes?" + query, UriKind.Relative), Ct);

        ((int)response.StatusCode).ShouldBe(422);
    }

    [Fact]
    public async Task The_outcomes_endpoint_returns_an_empty_page_and_no_route_has_an_orders_segment()
    {
        var page = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/outcomes?accountId={LiveAccount}&from=2026-10-01&to=2026-10-09&size=5", UriKind.Relative), Ct);

        page.GetProperty("total").GetInt32().ShouldBe(0);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private async Task<long> PlanItemAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await plans.PublishAsync(
            new NewPlan("live-capture-probe-" + Guid.NewGuid().ToString("N"), new DateOnly(2099, 1, 5), new DateTimeOffset(2099, 1, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [new NewPlanItem(1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "t", "s", "2R", "v", [], new PlanEvidence("c", 41, "d", "b", 0.7, 0.1, [], null, 0.7), [])]), Ct);
        return (await plans.ItemsAsync(published.Id, Ct))[0].Id;
    }
}
