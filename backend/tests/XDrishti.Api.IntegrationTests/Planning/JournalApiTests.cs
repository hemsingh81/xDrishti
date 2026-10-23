using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-14 on a real database: a fixture day of fills the portfolio sync stored is matched to approved tickets by account, instrument, side and day, the rest is discretionary,
/// finished round trips get a fill grade beside the plan grade, running again writes nothing new, and the database refuses what the tables refuse.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class JournalApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid Paper = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7b01");
    private static readonly Guid Other = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7b02");

    // A session far in the future: the database stamps an approval with its own clock and refuses one at or after the cutoff.
    private static readonly DateOnly Session = new(2099, 3, 5);
    private static readonly DateTimeOffset AfterSession = new(2099, 3, 5, 16, 0, 0, TimeSpan.Zero);

    private int _seq;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, slug, client) in new[] { (Paper, "probe-j1", "1000000081"), (Other, "probe-j2", "1000000082") })
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES (@id, @slug, @slug, 'Dhan', @client, current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                """,
                ("id", id), ("slug", slug), ("client", client));
        }
    }

    private static NewPlanItem Item(int rank, TicketGroup group, string symbol, string side, params Guid[] accounts) =>
        new(
            rank, group, "NSE:" + symbol, side, "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30",
            group == TicketGroup.Primary ? [] : ["rank_below_cut"],
            new PlanEvidence("ORB|15m|" + side + "|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.52, 0.12, [], null),
            [.. accounts.Select(a => new NewAllocation(a, "paper", 10))]);

    private static NewPlan Plan(params NewPlanItem[] items) =>
        new("plan-2099-03-05-journal", Session, new DateTimeOffset(2099, 3, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, items);

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Plan: RELIANCE buy (approved), TCS sell (approved), INFY buy (never approved), all allocated to the paper account.</summary>
    private async Task<(long Plan, long Reliance, long Tcs, long Infy)> StoredPlanAsync()
    {
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(
            Plan(Item(1, TicketGroup.Primary, "RELIANCE", "BUY", Paper), Item(2, TicketGroup.Reserve, "TCS", "SELL", Paper), Item(3, TicketGroup.Primary, "INFY", "BUY", Paper, Other)), Ct).AsTask());
        var items = await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask());
        foreach (var id in new[] { items[0].Id, items[1].Id })
        {
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(id, TicketState.Approved, "owner:me", "ok"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        }

        return (published.Id, items[0].Id, items[1].Id, items[2].Id);
    }

    private async Task FillAsync(Guid account, string symbol, string side, int hour, long qty, decimal price, DateOnly? date = null, string source = "broker", bool voided = false)
    {
        await using var connection = await OpenAsync();
        var n = Interlocked.Increment(ref _seq).ToString(CultureInfo.InvariantCulture);
        await ExecuteAsync(
            connection,
            """
            INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, trade_time, qty, price, charges, source, seq, external_id, created_by, created_at, voided_at, voided_by)
            VALUES (@id, @account, @symbol, 'NSE', @side, @date, @time, @qty, @price, 0, @source, 1, @ext, 'test', now(), CASE WHEN @voided THEN now() END, CASE WHEN @voided THEN 'test' END)
            """,
            ("id", Guid.NewGuid()), ("account", account), ("symbol", symbol), ("side", side), ("date", date ?? Session), ("time", new TimeOnly(hour, 0)), ("qty", qty), ("price", price), ("source", source), ("ext", "t" + n), ("voided", voided));
    }

    private Task<int> GradePlanAsync(long plan, long item, double entry, double stop) =>
        WithAsync(p => p.GetRequiredService<IGradeStore>().InsertAsync(
            [new NewGrade(plan, item, Session, new TicketGrade(GradeStatus.Graded, 2, 0.5, 2.5, "Target", entry, entry + 4, "gross, no charges", "x", stop), null, DateTimeOffset.UnixEpoch)], Ct).AsTask());

    private async Task<JournalOutcome> JournalAsync()
    {
        Factory.Clock.Pin(AfterSession);
        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<JournalPlansCommand, JournalOutcome>>().HandleAsync(new JournalPlansCommand(Session), Ct).AsTask());
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<long> CountAsync(string table) => await ScalarAsync<long>("SELECT count(*) FROM " + table);

    private async Task<string?> StateOfAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException e)
        {
            return e.SqlState;
        }
    }

    private async Task SeedFixtureDayAsync()
    {
        await FillAsync(Paper, "RELIANCE", "buy", 10, 10, 100.5m);
        await FillAsync(Paper, "RELIANCE", "sell", 13, 10, 104m);
        await FillAsync(Paper, "TCS", "sell", 11, 5, 200m);
        await FillAsync(Paper, "INFY", "buy", 10, 7, 50m); // its ticket was never approved
        await FillAsync(Other, "RELIANCE", "buy", 10, 3, 100m); // another account, which has an allocation but not for this symbol
        await FillAsync(Paper, "RELIANCE", "buy", 11, 99, 100m, source: "manual"); // typed in, not from the sync
        await FillAsync(Paper, "RELIANCE", "buy", 12, 98, 100m, voided: true); // voided
        await FillAsync(Paper, "RELIANCE", "buy", 12, 97, 100m, date: Session.AddDays(-1)); // another day
    }

    [Fact]
    public async Task A_fixture_day_is_matched_by_account_instrument_side_and_day_and_graded_beside_the_plan_grade()
    {
        var (plan, reliance, tcs, _) = await StoredPlanAsync();
        await SeedFixtureDayAsync();
        await GradePlanAsync(plan, reliance, 100, 98);

        var outcome = await JournalAsync();

        outcome.Tickets.ShouldBe(2);
        outcome.Entries.ShouldBe(2); // RELIANCE buy, TCS sell
        outcome.Exits.ShouldBe(1); // the RELIANCE sell
        outcome.Discretionary.ShouldBe(2); // INFY (unapproved) and the other account
        outcome.Unmatched.ShouldBeEmpty();
        (await CountAsync("trading.journal_entries")).ShouldBe(2);
        (await CountAsync("trading.journal_fills")).ShouldBe(5);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_fills WHERE role = 'discretionary' AND entry_id IS NULL")).ShouldBe(2);

        // RELIANCE: in 10 at 100.5, out at 104, plan entry 100 and stop 98 => slippage 0.5 = 0.25R, R = 35 / 20 = 1.75. The TCS short is open, and has no plan grade.
        outcome.GradesWritten.ShouldBe(1);
        outcome.GradesPending.ShouldBe(1);
        (await ScalarAsync<double>("SELECT r FROM trading.fill_grades")).ShouldBe(1.75, 1e-9);
        (await ScalarAsync<double>("SELECT slippage_r FROM trading.fill_grades")).ShouldBe(0.25, 1e-9);
        (await ScalarAsync<string>("SELECT basis FROM trading.fill_grades")).ShouldBe("realised fills, not simulated");
        (await ScalarAsync<long>($"SELECT count(*) FROM trading.journal_entries WHERE item_id = {tcs}")).ShouldBe(1);
    }

    [Fact]
    public async Task Running_again_writes_nothing_new_and_a_grade_that_was_pending_is_written_once_its_plan_grade_exists()
    {
        var (plan, reliance, tcs, _) = await StoredPlanAsync();
        await SeedFixtureDayAsync();
        await GradePlanAsync(plan, reliance, 100, 98);
        await JournalAsync();
        var firstGrade = await ScalarAsync<double>("SELECT r FROM trading.fill_grades");

        var again = await JournalAsync();
        await FillAsync(Paper, "TCS", "buy", 14, 5, 197m); // covers the short
        await GradePlanAsync(plan, tcs, 200, 204);
        var covered = await JournalAsync();

        again.Written.ShouldBe(0);
        again.GradesWritten.ShouldBe(0);
        (await CountAsync("trading.journal_entries")).ShouldBe(2);
        (await CountAsync("trading.journal_fills")).ShouldBe(6); // one new fill, the cover
        covered.Written.ShouldBe(1);
        covered.GradesWritten.ShouldBe(1);
        (await CountAsync("trading.fill_grades")).ShouldBe(2);
        (await ScalarAsync<double>($"SELECT r FROM trading.fill_grades g JOIN trading.journal_entries e ON e.id = g.entry_id WHERE e.item_id = {reliance}")).ShouldBe(firstGrade);
        // Short 5 at 200, covered at 197: gross 15, risk 4 per share => 0.75R; sold at the plan entry => no slippage.
        (await ScalarAsync<double>($"SELECT r FROM trading.fill_grades g JOIN trading.journal_entries e ON e.id = g.entry_id WHERE e.item_id = {tcs}")).ShouldBe(0.75, 1e-9);
    }

    [Fact]
    public async Task Two_approved_tickets_that_fit_one_fill_leave_it_unmatched_and_unstored()
    {
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(
            Plan(Item(1, TicketGroup.Primary, "RELIANCE", "BUY", Paper), Item(2, TicketGroup.Primary, "RELIANCE", "BUY", Paper)), Ct).AsTask());
        foreach (var item in await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask()))
        {
            await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(item.Id, TicketState.Approved, "owner:me", "ok"), Ct).AsTask());
        }

        await FillAsync(Paper, "RELIANCE", "buy", 10, 10, 100m);

        var outcome = await JournalAsync();

        outcome.Unmatched.Count.ShouldBe(1);
        (await CountAsync("trading.journal_fills")).ShouldBe(0);
        (await CountAsync("trading.journal_entries")).ShouldBe(0);
    }

    [Fact]
    public async Task The_fills_are_only_read_the_portfolio_and_the_ticket_states_are_unchanged()
    {
        var (plan, reliance, _, _) = await StoredPlanAsync();
        await SeedFixtureDayAsync();
        var before = (await CountAsync("portfolio.transactions"), await CountAsync("trading.plan_events"));

        await JournalAsync();

        (await CountAsync("portfolio.transactions"), await CountAsync("trading.plan_events")).ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM portfolio.transactions WHERE voided_at IS NOT NULL")).ShouldBe(1);
        (await WithAsync(p => p.GetRequiredService<IPlanStore>().FindItemAsync(reliance, Ct).AsTask()))!.State.ShouldBe(TicketState.Approved);
        _ = plan;
    }

    [Fact]
    public async Task The_endpoint_reads_the_journal_with_both_labels_and_the_discretionary_fills_apart()
    {
        var (plan, reliance, _, _) = await StoredPlanAsync();
        await SeedFixtureDayAsync();
        await GradePlanAsync(plan, reliance, 100, 98);
        await JournalAsync();
        await ResignInAsync();

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/journal", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/plans/999999/journal", UriKind.Relative), Ct);

        view.GetProperty("fillBasis").GetString().ShouldBe("realised fills, not simulated");
        view.GetProperty("planBasis").GetString().ShouldBe("simulated on plan prices");
        view.GetProperty("discretionary").GetArrayLength().ShouldBe(2);
        var entries = view.GetProperty("entries").EnumerateArray().ToList();
        entries.Count.ShouldBe(2);
        entries[0].GetProperty("symbol").GetString().ShouldBe("NSE:RELIANCE");
        entries[0].GetProperty("status").GetString().ShouldBe("graded");
        entries[0].GetProperty("fill").GetProperty("r").GetDouble().ShouldBe(1.75, 1e-9);
        entries[0].GetProperty("planR").GetDouble().ShouldBe(2);
        entries[1].GetProperty("status").GetString().ShouldBe("open");
        view.GetProperty("ticketsWithoutFills").GetInt32().ShouldBe(0);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_database_refuses_a_second_link_for_one_fill_an_entry_without_an_allocation_and_a_changed_label()
    {
        var (plan, _, tcs, infy) = await StoredPlanAsync();
        await SeedFixtureDayAsync();
        await JournalAsync();
        var fill = await ScalarAsync<Guid>("SELECT transaction_id FROM trading.journal_fills WHERE role = 'entry' LIMIT 1");
        var entry = await ScalarAsync<long>("SELECT id FROM trading.journal_entries LIMIT 1");

        // One row per fill: a second link, even with another role, is refused.
        (await StateOfAsync("INSERT INTO trading.journal_fills (transaction_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@t, @e, 'exit', 1, 'x', @d, now())", ("t", fill), ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.UniqueViolation);

        // A discretionary fill has no entry and any other has one.
        var spare = Guid.NewGuid();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, qty, price, charges, source, seq, external_id, created_by, created_at) VALUES (@id, @a, 'X', 'NSE', 'buy', @d, 1, 1, 0, 'broker', 1, 'spare', 't', now())", ("id", spare), ("a", Paper), ("d", Session));
        }

        (await StateOfAsync("INSERT INTO trading.journal_fills (transaction_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@t, @e, 'discretionary', 1, 'x', @d, now())", ("t", spare), ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync("INSERT INTO trading.journal_fills (transaction_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@t, NULL, 'entry', 1, 'x', @d, now())", ("t", spare), ("d", Session))).ShouldBe(PostgresErrorCodes.CheckViolation);

        // An entry needs a real allocation (the other account has none for TCS) and a real item of its own plan.
        (await StateOfAsync("INSERT INTO trading.journal_entries (plan_id, item_id, account_id, for_date, created_at) VALUES (@p, @i, @a, @d, now())", ("p", plan), ("i", tcs), ("a", Other), ("d", Session))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync("INSERT INTO trading.journal_entries (plan_id, item_id, account_id, for_date, created_at) VALUES (@p, @i, @a, @d, now())", ("p", plan + 1000), ("i", infy), ("a", Paper), ("d", Session))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);

        // A fill grade carries its label and an R exactly when it was measured.
        (await StateOfAsync("INSERT INTO trading.fill_grades (entry_id, status, qty, entry_price, exit_price, gross_pnl, charges, basis, detail, graded_at) VALUES (@e, 'graded', 1, 1, 1, 0, 0, 'simulated on plan prices', 'x', now())", ("e", entry))).ShouldNotBeNull();
    }
}
