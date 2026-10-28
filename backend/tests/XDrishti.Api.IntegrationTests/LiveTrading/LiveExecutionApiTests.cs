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

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-02 on a real database: the four tables refuse what the migration lists (a slippage without its reference, a delay without an execution part, a mean on an insufficient row, a score that is not a value, a second execution for one outcome), the store writes each row once
/// (one execution per outcome, one class per order, one snapshot per week and key), and the four GET endpoints answer 404 / 422 / 200 as the contract says, including on an empty database.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveExecutionApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid LiveAccount = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a61");
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static int _plans;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES (@id, 'probe-exec', 'Probe', 'Dhan', '1000000081', current_date, '#1A2B3C', now(), now(), false, false, false, 200)",
            ("id", LiveAccount));
        await ExecuteAsync(connection, "INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments) VALUES (@id, true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)", ("id", LiveAccount));
        await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES (@id, false, true)", ("id", LiveAccount));
    }

    private async Task<TResult> With<TResult>(Func<ILiveExecutionStore, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ILiveExecutionStore>());
    }

    private async Task<(long Item, long Outcome)> OutcomeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var planDate = new DateOnly(2099, 1, 5).AddDays(Interlocked.Increment(ref _plans));
        var published = await plans.PublishAsync(
            new NewPlan("live-exec-probe-" + Guid.NewGuid().ToString("N"), planDate, new DateTimeOffset(planDate.ToDateTime(new TimeOnly(3, 35)), TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [new NewPlanItem(1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "t", "s", "2R", "v", [], new PlanEvidence("c", 41, "d", "b", 0.7, 0.1, [], null, 0.7), [])]), Ct);
        var item = (await plans.ItemsAsync(published.Id, Ct))[0].Id;
        var capture = scope.ServiceProvider.GetRequiredService<ILiveCaptureStore>();
        await capture.InsertOutcomesAsync([new NewTicketOutcome(item, LiveAccount, Day, 40, new TicketOutcome(OutcomeCodes.Filled, 40, 40, null, [], null, "graded", "present", OutcomeCodes.Protected, null))], Ct);
        var outcome = (await capture.OutcomesOfDateAsync(LiveAccount, Day, Ct)).Single(o => o.ItemId == item).Id;
        return (item, outcome);
    }

    private static NewLiveExecution Execution(long outcome, long item, string kind = "ticket_level", double? delay = 0.20) => new(
        outcome, LiveAccount, item, Day, "RELIANCE", "buy", "delivery", "CNC", "L1", 640m, 0.000782m, 0.05m, kind, kind == "unavailable" ? null : 1250m, 10m, delay is null ? null : 1250.20m, 1250.475m, 40,
        kind == "unavailable" ? null : 0.475, kind == "unavailable" ? null : delay, kind == "unavailable" || delay is null ? null : 0.475 - delay, 9.5, kind == "unavailable" ? null : 3.8, 0.0475, null, null, 70, 1250m, 1264m, 1240m, 0, 1262m, 40, 38.40m);

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
    public async Task An_execution_is_stored_once_per_outcome_read_back_exactly_and_listed()
    {
        var (item, outcome) = await OutcomeAsync();

        (await With(s => s.InsertExecutionsAsync([Execution(outcome, item)], Ct).AsTask())).ShouldBe(1);
        (await With(s => s.InsertExecutionsAsync([Execution(outcome, item)], Ct).AsTask())).ShouldBe(0);

        var page = await With(s => s.ListExecutionsAsync(LiveAccount, Day, Day, 0, 10, Ct).AsTask());
        page.Total.ShouldBe(1);
        var row = page.Items.Single().Row;
        (row.FillVwap, row.FillQty, row.Bucket, row.ReferenceKind, row.PlanExit, row.LiveCharges, row.EntryDelaySeconds).ShouldBe((1250.475m, 40L, "L1", "ticket_level", 1264m, 38.40m, 70));
        row.SlipTotalBps.ShouldBe(3.8);
        (await With(s => s.FindExecutionAsync(page.Items.Single().Id, Ct).AsTask())).ShouldNotBeNull();
        (await With(s => s.ExecutionOutcomeIdsAsync(LiveAccount, Day, Ct).AsTask())).ShouldBe([outcome]);
        (await With(s => s.ExecutionSummaryAsync(Day, Ct).AsTask())).ShouldBe((1, Day, Day));
    }

    [Fact]
    public async Task The_execution_table_refuses_a_slippage_without_its_reference_a_split_that_does_not_add_up_and_a_second_row_for_one_outcome()
    {
        var (item, outcome) = await OutcomeAsync();
        await With(s => s.InsertExecutionsAsync([Execution(outcome, item)], Ct).AsTask());
        const string insert = "INSERT INTO trading.live_executions (outcome_id, account_id, item_id, for_date, symbol, side, style, product, bucket, reference_kind, reference_price, fill_vwap, fill_qty, slip_total, slip_delay, slip_exec, slip_total_bps) VALUES (@o, @a, @i, '2026-11-03', 'X', 'buy', 'delivery', 'CNC', 'L1', @k, @rp, 100, 10, @st, @sd, @se, @sb)";
        object Null() => DBNull.Value;

        (await SqlStateAsync(insert, ("o", outcome), ("a", LiveAccount), ("i", item), ("k", "ticket_level"), ("rp", 99m), ("st", 1.0), ("sd", 0.4), ("se", 0.6), ("sb", 100.0))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(insert, ("o", outcome + 1000), ("a", LiveAccount), ("i", item), ("k", "ticket_level"), ("rp", 99m), ("st", 1.0), ("sd", 0.4), ("se", 0.6), ("sb", 100.0))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        var (item2, outcome2) = await OutcomeAsync();
        (await SqlStateAsync(insert, ("o", outcome2), ("a", LiveAccount), ("i", item2), ("k", "unavailable"), ("rp", Null()), ("st", 1.0), ("sd", Null()), ("se", Null()), ("sb", Null()))).ShouldBe(PostgresErrorCodes.CheckViolation); // a slippage with no reference
        (await SqlStateAsync(insert, ("o", outcome2), ("a", LiveAccount), ("i", item2), ("k", "ticket_level"), ("rp", Null()), ("st", 1.0), ("sd", Null()), ("se", Null()), ("sb", Null()))).ShouldBe(PostgresErrorCodes.CheckViolation); // a reference kind with no price
        (await SqlStateAsync(insert, ("o", outcome2), ("a", LiveAccount), ("i", item2), ("k", "ticket_level"), ("rp", 99m), ("st", 1.0), ("sd", 0.4), ("se", Null()), ("sb", Null()))).ShouldBe(PostgresErrorCodes.CheckViolation); // a delay without an execution part
        (await SqlStateAsync(insert, ("o", outcome2), ("a", LiveAccount), ("i", item2), ("k", "ticket_level"), ("rp", 99m), ("st", 1.0), ("sd", 0.4), ("se", 0.5), ("sb", Null()))).ShouldBe(PostgresErrorCodes.CheckViolation); // delay + execution is not the total
        (await SqlStateAsync(insert, ("o", outcome2), ("a", LiveAccount), ("i", item2), ("k", "ticket_level"), ("rp", 99m), ("st", 1.0), ("sd", 0.4), ("se", 0.6), ("sb", 100.0))).ShouldBeNull();
    }

    [Fact]
    public async Task An_order_class_is_stored_once_per_order_and_refuses_a_class_outside_the_six()
    {
        var (_, outcome) = await OutcomeAsync();
        var row = new LiveOrderClassRow(LiveAccount, "o1", outcome, Day, "RELIANCE", "L1", "LIMIT", 40, 40, "full");

        (await With(s => s.InsertOrderClassesAsync([row], Ct).AsTask())).ShouldBe(1);
        (await With(s => s.InsertOrderClassesAsync([row], Ct).AsTask())).ShouldBe(0);

        (await With(s => s.ClassifiedOrderIdsAsync(LiveAccount, Day, Ct).AsTask())).ShouldBe(["o1"]);
        (await With(s => s.OrderClassesUntilAsync(Day, Ct).AsTask())).Single().Class.ShouldBe("full");
        (await SqlStateAsync("INSERT INTO trading.live_order_classes (account_id, broker_order_id, outcome_id, for_date, symbol, bucket, order_type, qty, filled_qty, class) VALUES (@a, 'o2', @o, '2026-11-03', 'X', 'L1', 'LIMIT', 10, 0, 'bogus')", ("a", LiveAccount), ("o", outcome))).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task One_snapshot_per_week_and_key_and_the_tables_refuse_a_mean_on_an_insufficient_row_and_a_score_that_is_not_a_value()
    {
        var hash = new string('b', 64);
        var insufficient = new NewSlippageEstimate(Monday, "L1", "buy", "intraday", "plan_grade", 9, "insufficient", "n = 9, at least 10 needed", null, null, null, null, null, null, null, null, null, hash);
        var value = insufficient with { Bucket = "L2", N = 24, State = "value", Reason = null, MeanBps = 3.0, MeanLo = 1.0, MeanHi = 5.0, MedianBps = 3.0, Proposal = "text" };
        var capacity = new NewCapacityScore(Monday, "account", "all", 20, 18, 0, 1, 1, 0, 0, 19.0 / 22.0, 0.7, 0.97, "value", null, 2, hash);

        (await With(s => s.InsertSlippageEstimatesAsync([insufficient, value], Ct).AsTask())).ShouldBe(2);
        (await With(s => s.InsertSlippageEstimatesAsync([insufficient, value], Ct).AsTask())).ShouldBe(0);
        (await With(s => s.InsertCapacityScoresAsync([capacity], Ct).AsTask())).ShouldBe(1);
        (await With(s => s.InsertCapacityScoresAsync([capacity], Ct).AsTask())).ShouldBe(0);

        var (week, rows) = await With(s => s.SlippageEstimatesAsync(null, Ct).AsTask());
        week.ShouldBe(Monday);
        rows.Select(r => (r.Bucket, r.State, r.MeanBps)).ShouldBe([("L1", "insufficient", (double?)null), ("L2", "value", 3.0)]);
        (await With(s => s.SlippageEstimatesAsync(Monday.AddDays(7), Ct).AsTask())).Rows.ShouldBeEmpty();
        (await With(s => s.CapacityScoresAsync(null, Ct).AsTask())).Rows.Single().Score.ShouldBe(19.0 / 22.0);

        const string slip = "INSERT INTO trading.slippage_estimates (week_start, bucket, side, style, reference_kind, n, state, mean_bps, mean_lo, mean_hi, input_hash) VALUES ('2026-11-09', 'L3', 'buy', 'intraday', @k, 9, @s, @m, @m, @m, @h)";
        (await SqlStateAsync(slip, ("k", "plan_grade"), ("s", "insufficient"), ("m", 1.0), ("h", hash))).ShouldBe(PostgresErrorCodes.CheckViolation); // no mean below the minimum
        (await SqlStateAsync(slip, ("k", "unavailable"), ("s", "value"), ("m", 1.0), ("h", hash))).ShouldBe(PostgresErrorCodes.CheckViolation); // a row with no reference is not an estimate
        const string cap = "INSERT INTO trading.capacity_scores (week_start, scope, scope_key, n_orders, n_full, n_partial, n_rejected, n_queue_miss, n_price_miss, n_unclassified, score, lo, hi, state, prior_strength, input_hash) VALUES ('2026-11-09', 'bucket', 'L1', 5, 3, 1, 1, 0, 0, 0, @score, @score, @score, @s, 10, @h)";
        (await SqlStateAsync(cap, ("score", 0.7), ("s", "insufficient"), ("h", hash))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(cap, ("score", DBNull.Value), ("s", "value"), ("h", hash))).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_four_endpoints_answer_an_empty_database_and_refuse_a_bad_input()
    {
        var executions = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/live-trading/executions?size=5", UriKind.Relative), Ct);
        var slippage = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/live-trading/slippage", UriKind.Relative), Ct);
        var capacity = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/live-trading/capacity", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/live-trading/waterfall?entryId=999", UriKind.Relative), Ct);
        using var notMonday = await Client.GetAsync(new Uri("/api/live-trading/slippage?weekStart=2026-11-03", UriKind.Relative), Ct);
        using var bigPage = await Client.GetAsync(new Uri("/api/live-trading/executions?size=201", UriKind.Relative), Ct);

        executions.GetProperty("total").GetInt32().ShouldBe(0);
        executions.GetProperty("evidence").GetProperty("bucketEdgesCr").GetArrayLength().ShouldBe(4);
        slippage.GetProperty("state").GetString().ShouldBe("empty");
        slippage.GetProperty("evidence").GetProperty("referenceKinds").GetArrayLength().ShouldBe(3);
        capacity.GetProperty("state").GetString().ShouldBe("empty");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ((int)notMonday.StatusCode).ShouldBe(422);
        ((int)bigPage.StatusCode).ShouldBe(422);
    }

    [Fact]
    public async Task A_stored_execution_is_served_with_its_numbers_and_a_waterfall_when_the_trip_closed()
    {
        var (item, outcome) = await OutcomeAsync();
        await With(s => s.InsertExecutionsAsync([Execution(outcome, item)], Ct).AsTask());
        var id = (await With(s => s.ListExecutionsAsync(LiveAccount, Day, Day, 0, 1, Ct).AsTask())).Items.Single().Id;

        var page = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/executions?accountId={LiveAccount}&from=2026-11-01&to=2026-11-05", UriKind.Relative), Ct);
        var waterfall = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/live-trading/waterfall?entryId={id}", UriKind.Relative), Ct);

        var first = page.GetProperty("items")[0];
        first.GetProperty("totalBps").GetProperty("number").GetDouble().ShouldBe(3.8, 1e-9);
        first.GetProperty("totalR").GetProperty("number").GetDouble().ShouldBe(0.0475, 1e-9);
        first.GetProperty("delayR").GetProperty("state").GetString().ShouldBe("unavailable"); // the row stores no R split
        first.GetProperty("delayR").GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        first.GetProperty("waterfallAvailable").GetBoolean().ShouldBeTrue();
        waterfall.GetProperty("state").GetString().ShouldBe("value");
        waterfall.GetProperty("basis").GetString().ShouldBe("net");
        waterfall.GetProperty("balanced").GetBoolean().ShouldBeTrue();
        waterfall.GetProperty("rows").GetArrayLength().ShouldBe(6);
    }
}
