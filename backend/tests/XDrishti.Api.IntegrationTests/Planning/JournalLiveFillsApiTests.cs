using System.Globalization;
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
/// Spec P11-01b on a real database: the intraday fills the live capture stored are journaled through the same matcher, once (a second run writes nothing; a delivery fill and a fill that has a stored twin are never read from the live table),
/// the database refuses a link that names both or neither source, a second link for one live fill, a link to another account's entry and the same trade through both sources, and a round trip without recorded charges is graded without an R.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class JournalLiveFillsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly Guid Live = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7c01");
    private static readonly Guid Other = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7c02");
    private static readonly DateOnly Session = new(2099, 3, 5);
    private static readonly DateTimeOffset AfterSession = new(2099, 3, 5, 16, 0, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, slug, client) in new[] { (Live, "probe-l1", "1000000091"), (Other, "probe-l2", "1000000092") })
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

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task<(long Plan, long Reliance)> StoredPlanAsync()
    {
        NewPlanItem item = new(
            1, TicketGroup.Primary, "NSE:RELIANCE", "BUY", "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", 41, "replayed history", "cell statistics only", 0.52, 0.12, [], null), [new NewAllocation(Live, "paper", 10)]);
        var plan = new NewPlan("plan-2099-03-05-live-journal", Session, new DateTimeOffset(2099, 3, 5, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var items = await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask());
        (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(items[0].Id, TicketState.Approved, "owner:me", "ok"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return (published.Id, items[0].Id);
    }

    private async Task<Guid> LiveFillAsync(Guid account, string tradeId, string side, int hour, long qty, decimal price, string product = "INTRADAY", decimal? charges = null)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO trading.live_fills (id, account_id, broker_trade_id, symbol, side, product, qty, price, trade_date, trade_time, source) VALUES (@id, @a, @t, 'RELIANCE', @side, @product, @qty, @price, @d, @time, 'trade_book')",
            ("id", id), ("a", account), ("t", tradeId), ("side", side), ("product", product), ("qty", qty), ("price", price), ("d", Session), ("time", new TimeOnly(hour, 0)));
        if (charges is { } c)
        {
            await ExecuteAsync(connection, "INSERT INTO trading.live_fill_charges (fill_id, charges) VALUES (@id, @c)", ("id", id), ("c", c));
        }

        return id;
    }

    private async Task<Guid> StoredTwinAsync(Guid account, string tradeId)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            "INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, trade_time, qty, price, charges, source, seq, external_id, created_by, created_at) VALUES (@id, @a, 'RELIANCE', 'NSE', 'buy', @d, '10:00', 10, 100.5, 0, 'broker', 1, @ext, 'test', now())",
            ("id", id), ("a", account), ("d", Session), ("ext", "t:" + Session.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" + tradeId));
        return id;
    }

    private async Task<JournalOutcome> JournalAsync()
    {
        Factory.Clock.Pin(AfterSession);
        var result = await WithAsync(p => p.GetRequiredService<ICommandHandler<JournalPlansCommand, JournalOutcome>>().HandleAsync(new JournalPlansCommand(Session), Ct).AsTask());
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

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

    [Fact]
    public async Task Intraday_live_fills_are_journaled_once_and_an_unrecorded_charge_gives_a_grade_with_no_r()
    {
        var (plan, reliance) = await StoredPlanAsync();
        var entry = await LiveFillAsync(Live, "E1", "buy", 10, 10, 100.5m);
        var exit = await LiveFillAsync(Live, "E2", "sell", 13, 10, 104m);
        await LiveFillAsync(Other, "E3", "buy", 10, 5, 100m); // another account with no allocation: not read
        await WithAsync(p => p.GetRequiredService<IGradeStore>().InsertAsync(
            [new NewGrade(plan, reliance, Session, new TicketGrade(GradeStatus.Graded, 2, 0.5, 2.5, "Target", 100, 104, "gross, no charges", "x", 98), null, DateTimeOffset.UnixEpoch)], Ct).AsTask());

        var first = await JournalAsync();

        first.Entries.ShouldBe(1);
        first.Exits.ShouldBe(1);
        first.Discretionary.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_fills WHERE live_fill_id IS NOT NULL AND transaction_id IS NULL")).ShouldBe(2);
        (await ScalarAsync<Guid>("SELECT live_fill_id FROM trading.journal_fills WHERE role = 'entry'")).ShouldBe(entry);
        (await ScalarAsync<Guid>("SELECT live_fill_id FROM trading.journal_fills WHERE role = 'exit'")).ShouldBe(exit);
        (await ScalarAsync<string>("SELECT status FROM trading.fill_grades")).ShouldBe("uncertain");
        (await ScalarAsync<string>("SELECT detail FROM trading.fill_grades")).ShouldContain("charges of an intraday live fill are not recorded");
        (await ScalarAsync<long>("SELECT count(*) FROM trading.fill_grades WHERE r IS NOT NULL")).ShouldBe(0);

        var second = await JournalAsync();

        second.Written.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_fills")).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_entries")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.fill_grades")).ShouldBe(1);
    }

    [Fact]
    public async Task Recorded_charges_let_the_live_round_trip_be_graded_in_r()
    {
        var (plan, reliance) = await StoredPlanAsync();
        await LiveFillAsync(Live, "E1", "buy", 10, 10, 100.5m, charges: 0m);
        await LiveFillAsync(Live, "E2", "sell", 13, 10, 104m, charges: 0m);
        await WithAsync(p => p.GetRequiredService<IGradeStore>().InsertAsync(
            [new NewGrade(plan, reliance, Session, new TicketGrade(GradeStatus.Graded, 2, 0.5, 2.5, "Target", 100, 104, "gross, no charges", "x", 98), null, DateTimeOffset.UnixEpoch)], Ct).AsTask());

        await JournalAsync();

        (await ScalarAsync<double>("SELECT r FROM trading.fill_grades")).ShouldBe(1.75, 1e-9);
    }

    [Fact]
    public async Task A_delivery_fill_and_a_fill_with_a_stored_twin_are_never_journaled_from_the_live_table()
    {
        await StoredPlanAsync();
        await LiveFillAsync(Live, "C1", "buy", 10, 10, 100.5m, product: "CNC"); // reaches the journal through the stored trade
        await LiveFillAsync(Live, "T1", "buy", 11, 4, 100m); // intraday, but the history sync stored the same trade
        var twin = await StoredTwinAsync(Live, "T1");

        var outcome = await JournalAsync();

        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_fills WHERE live_fill_id IS NOT NULL")).ShouldBe(0);
        outcome.Entries.ShouldBe(1); // the stored twin is the entry, once
        (await ScalarAsync<Guid>("SELECT transaction_id FROM trading.journal_fills WHERE role = 'entry'")).ShouldBe(twin);
    }

    [Fact]
    public async Task The_database_refuses_both_or_neither_source_a_second_link_a_foreign_account_and_the_same_trade_through_both_sources()
    {
        await StoredPlanAsync();
        var entryFill = await LiveFillAsync(Live, "E1", "buy", 10, 10, 100.5m);
        await JournalAsync();
        var entry = await ScalarAsync<long>("SELECT id FROM trading.journal_entries");
        var stored = await StoredTwinAsync(Live, "X9");
        var spare = await LiveFillAsync(Live, "S1", "sell", 14, 1, 100m);
        var foreign = await LiveFillAsync(Other, "F1", "sell", 14, 1, 100m);
        const string head = "INSERT INTO trading.journal_fills (transaction_id, live_fill_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES ";

        (await StateOfAsync(head + "(@t, @l, @e, 'exit', 1, 'x', @d, now())", ("t", stored), ("l", spare), ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.CheckViolation); // both
        (await StateOfAsync(head + "(NULL, NULL, @e, 'exit', 1, 'x', @d, now())", ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.CheckViolation); // neither
        (await StateOfAsync(head + "(NULL, @l, @e, 'exit', 1, 'x', @d, now())", ("l", entryFill), ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.UniqueViolation); // a second link for one live fill
        (await StateOfAsync(head + "(NULL, @l, @e, 'exit', 1, 'x', @d, now())", ("l", foreign), ("e", entry), ("d", Session))).ShouldBe(PostgresErrorCodes.CheckViolation); // another account's fill
        (await StateOfAsync(head + "(NULL, @l, NULL, 'discretionary', 1, 'x', @d, now())", ("l", Guid.NewGuid()), ("d", Session))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation); // no such live fill

        // The same trade through both sources, in either order of arrival.
        var twinLive = await LiveFillAsync(Live, "X9", "buy", 10, 10, 100.5m);
        (await StateOfAsync(head + "(@t, NULL, NULL, 'discretionary', 1, 'x', @d, now())", ("t", stored), ("d", Session))).ShouldBeNull();
        (await StateOfAsync(head + "(NULL, @l, NULL, 'discretionary', 1, 'x', @d, now())", ("l", twinLive), ("d", Session))).ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_stored_trade_that_arrives_after_its_live_twin_was_journaled_is_not_journaled_and_no_run_fails()
    {
        await StoredPlanAsync();
        var live = await LiveFillAsync(Live, "T1", "buy", 10, 10, 100.5m);
        (await JournalAsync()).Entries.ShouldBe(1);
        await StoredTwinAsync(Live, "T1"); // the history sync stores the same trade later

        var again = await JournalAsync(); // must not throw (the trigger would refuse a second link)
        var third = await JournalAsync();

        again.Written.ShouldBe(0);
        third.Written.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.journal_fills")).ShouldBe(1);
        (await ScalarAsync<Guid>("SELECT live_fill_id FROM trading.journal_fills")).ShouldBe(live);
    }

    [Fact]
    public async Task A_voided_stored_twin_does_not_block_the_live_fill_and_a_voided_journaled_twin_does_not_block_it_in_the_trigger()
    {
        await StoredPlanAsync();
        var twin = await StoredTwinAsync(Live, "V1");
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE portfolio.transactions SET voided_at = now(), voided_by = 'test' WHERE id = @id", ("id", twin));
        }

        var live = await LiveFillAsync(Live, "V1", "buy", 10, 10, 100.5m);
        (await JournalAsync()).Entries.ShouldBe(1);
        (await ScalarAsync<Guid>("SELECT live_fill_id FROM trading.journal_fills")).ShouldBe(live);
        (await JournalAsync()).Written.ShouldBe(0);

        // The trigger agrees: a stored trade, journaled and then voided, does not stop its live twin from being linked.
        var stored = await StoredTwinAsync(Live, "V2");
        var entry = await ScalarAsync<long>("SELECT id FROM trading.journal_entries");
        (await StateOfAsync("INSERT INTO trading.journal_fills (transaction_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@t, @e, 'exit', 1, 'x', @d, now())", ("t", stored), ("e", entry), ("d", Session))).ShouldBeNull();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE portfolio.transactions SET voided_at = now(), voided_by = 'test' WHERE id = @id", ("id", stored));
        }

        var liveV2 = await LiveFillAsync(Live, "V2", "sell", 11, 1, 100m);
        (await StateOfAsync("INSERT INTO trading.journal_fills (live_fill_id, entry_id, role, qty, reason, for_date, journaled_at) VALUES (@l, @e, 'exit', 1, 'x', @d, now())", ("l", liveV2), ("e", entry), ("d", Session))).ShouldBeNull();
    }
}
