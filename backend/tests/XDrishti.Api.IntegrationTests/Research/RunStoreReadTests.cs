using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Criteria 10, 11, 12 and 18b of P3-10: the checks of the columns, the list and the trade pages, the same-entries join and the read path against injection.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunStoreReadTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<T> WithStore<T>(Func<IRunStore, Task<T>> action) => StoreKit.WithStoreAsync(factory.Services, action);

    private async Task<SimulationRun> StoredAsync(bool costed = false, string strategy = "ORB", DateOnly? to = null, bool fixtureEdges = true, DateTimeOffset? at = null)
    {
        var run = StoredRunFixtures.Run(costed, to: to ?? RunStoreRoundTripTests.NextTo(), strategy: strategy, fixtureEdges: fixtureEdges);
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run, at), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        return run;
    }

    private async Task<List<RunTrade>> AllPagesAsync(string key, TradePageFilter filter, int size)
    {
        var all = new List<RunTrade>();
        string? cursor = null;
        do
        {
            var page = await WithStore(s => s.PageTradesAsync(key, filter, new PageRequest(size, cursor), Ct).AsTask());
            page.IsSuccess.ShouldBeTrue(page.IsFailure ? page.Error.Message : string.Empty);
            page.Value.Items.Count.ShouldBeLessThanOrEqualTo(size);
            all.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);
        return all;
    }

    // criterion 10: the columns hold the Domain's constraints

    [Theory]
    [InlineData("UPDATE research.sim_trades SET side = 'long' WHERE run_id = @id", "ck_sim_trades_side", false)]
    [InlineData("UPDATE research.sim_trades SET exit_reason = 'timeout' WHERE run_id = @id", "ck_sim_trades_exit_reason", false)]
    [InlineData("UPDATE research.sim_trades SET fills = 'guess' WHERE run_id = @id", "ck_sim_trades_fills", false)]
    [InlineData("UPDATE research.sim_trades SET costs = 'maybe' WHERE run_id = @id", "ck_sim_trades_costs", false)]
    [InlineData("UPDATE research.sim_trades SET entry_fill = 'late' WHERE run_id = @id", "ck_sim_trades_entry_fill", false)]
    [InlineData("UPDATE research.sim_trades SET trigger_timeframe = '7m' WHERE run_id = @id", "ck_sim_trades_trigger_timeframe", false)]
    [InlineData("UPDATE research.sim_trades SET cost_segment = 'futures' WHERE run_id = @id AND costs = 'charged'", "ck_sim_trades_cost_segment", true)]
    [InlineData("UPDATE research.sim_trades SET costs = 'charged' WHERE run_id = @id AND costs = 'none'", "ck_sim_trades_costs_charged", false)]
    [InlineData("UPDATE research.sim_trades SET cost_total = 1 WHERE run_id = @id AND costs = 'none'", "ck_sim_trades_costing_all_or_nothing", false)]
    [InlineData("UPDATE research.sim_trades SET exit_gst = NULL WHERE run_id = @id AND costs = 'charged'", "ck_sim_trades_costing_all_or_nothing", true)]
    public async Task A_row_that_breaks_a_closed_set_or_the_all_or_nothing_costing_is_refused(string update, string constraint, bool costed)
    {
        var run = await StoredAsync(costed, fixtureEdges: false);
        var id = await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT id FROM research.sim_runs WHERE run_key = @k", Ct, ("k", run.RunKey));

        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(factory.AdminConnectionString, update.Replace("@id", id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), Ct));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe(constraint);
    }

    [Theory]
    [InlineData("UPPER(run_key)")]
    [InlineData("repeat('a', 63)")]
    public async Task A_run_key_with_an_upper_case_letter_or_63_characters_is_refused(string value)
    {
        var run = await StoredAsync(fixtureEdges: false);
        var statement = $"UPDATE research.sim_runs SET run_key = {value} WHERE run_key = '{run.RunKey}'";

        var failure = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(factory.AdminConnectionString, statement, Ct));

        failure.ConstraintName.ShouldBe("ck_sim_runs_run_key");
    }

    // criterion 11: the read path

    [Fact]
    public async Task An_unknown_key_finds_nothing()
    {
        (await WithStore(s => s.FindAsync(new string('0', 64), Ct).AsTask())).ShouldBeNull();
        (await WithStore(s => s.ReadRecordTextAsync(new string('0', 64), Ct).AsTask())).ShouldBeNull();
        (await WithStore(async s => await s.StreamTradesAsync(new string('0', 64), false, Ct).ToListAsync(Ct))).ShouldBeEmpty();
        (await WithStore(s => s.PageTradesAsync(new string('0', 64), new TradePageFilter(), new PageRequest(5), Ct).AsTask())).Error.Code.ShouldBe("store.run_not_found");
    }

    [Fact]
    public async Task The_list_is_newest_first_filters_by_strategy_engine_and_costing_and_pages_without_repeating_or_skipping()
    {
        const string strategy = "LISTTEST";
        var t0 = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
        var first = await StoredAsync(strategy: strategy, at: t0, fixtureEdges: false);
        var second = await StoredAsync(costed: true, strategy: strategy, at: t0.AddMinutes(1), fixtureEdges: false);
        var third = await StoredAsync(strategy: strategy, at: t0.AddMinutes(2), fixtureEdges: false);
        await StoredAsync(strategy: "OTHERLIST", at: t0.AddMinutes(3), fixtureEdges: false);

        var pageOne = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy), new PageRequest(2), Ct).AsTask());
        var pageTwo = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy), new PageRequest(2, pageOne.Value.NextCursor), Ct).AsTask());
        var costed = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy, Costed: true), new PageRequest(10), Ct).AsTask());
        var gross = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy, Costed: false), new PageRequest(10), Ct).AsTask());
        var engine = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy, EngineVersion: "sim-5"), new PageRequest(10), Ct).AsTask());
        var otherEngine = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy, EngineVersion: "sim-4"), new PageRequest(10), Ct).AsTask());
        var window = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: strategy, CreatedFrom: t0.AddMinutes(1), CreatedTo: t0.AddMinutes(2)), new PageRequest(10), Ct).AsTask());

        pageOne.Value.Items.Select(h => h.RunKey).ShouldBe([third.RunKey, second.RunKey]);
        pageOne.Value.NextCursor.ShouldNotBeNull();
        pageTwo.Value.Items.Select(h => h.RunKey).ShouldBe([first.RunKey]);
        pageTwo.Value.NextCursor.ShouldBeNull();
        costed.Value.Items.Select(h => h.RunKey).ShouldBe([second.RunKey]);
        gross.Value.Items.Select(h => h.RunKey).ShouldBe([third.RunKey, first.RunKey]);
        engine.Value.Items.Count.ShouldBe(3);
        otherEngine.Value.Items.ShouldBeEmpty();
        window.Value.Items.Select(h => h.RunKey).ShouldBe([second.RunKey]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task A_page_size_of_0_or_1001_is_refused_by_the_adapter(int size)
    {
        var run = await StoredAsync(fixtureEdges: false);

        var trades = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(size), Ct).AsTask());
        var runs = await WithStore(s => s.ListAsync(new RunListFilter(), new PageRequest(size), Ct).AsTask());

        trades.Error.Code.ShouldBe("store.page_invalid");
        runs.Error.Code.ShouldBe("store.page_invalid");
    }

    [Theory]
    [InlineData("not a cursor")]
    [InlineData("")]
    [InlineData("dDE6MDo5OTk5OTk5OTk5OTk5OTk5OTk5OTk")]
    [InlineData("cjE6MTox")]
    public async Task A_malformed_or_foreign_cursor_is_refused(string cursor)
    {
        var run = await StoredAsync(fixtureEdges: false);

        var trades = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(5, cursor), Ct).AsTask());

        trades.IsFailure.ShouldBeTrue();
        trades.Error.Code.ShouldBe("store.page_invalid");
    }

    [Fact]
    public async Task A_cursor_of_the_list_of_runs_is_not_valid_for_the_trades_of_a_run()
    {
        var run = await StoredAsync(fixtureEdges: false);
        var page = await WithStore(s => s.ListAsync(new RunListFilter(StrategyId: "ORB"), new PageRequest(1), Ct).AsTask());
        page.Value.NextCursor.ShouldNotBeNull();

        var trades = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(5, page.Value.NextCursor), Ct).AsTask());

        trades.Error.Code.ShouldBe("store.page_invalid");
    }

    [Fact]
    public async Task Each_filter_of_the_trade_page_gives_the_rows_a_linq_filter_gives_in_the_records_order_with_no_row_repeated_or_skipped()
    {
        var run = await StoredAsync(costed: true);
        var all = run.Trades.Select(t => (t, false)).Concat(run.OpenAtEnd.Select(t => (t, true))).ToList();
        var symbol = run.Trades[0].Trade.Symbol;
        var from = run.Trades[0].Trade.TradeDate.AddDays(1);
        var to = from.AddDays(2);
        var cases = new (TradePageFilter Filter, Func<(RunTrade Trade, bool Open), bool> Predicate)[]
        {
            (new TradePageFilter(), _ => true),
            (new TradePageFilter(Side: Side.Sell), x => x.Trade.Trade.Side == Side.Sell),
            (new TradePageFilter(Symbol: symbol), x => x.Trade.Trade.Symbol == symbol),
            (new TradePageFilter(ExitReason: ExitReason.Target), x => x.Trade.Trade.ExitReason == ExitReason.Target),
            (new TradePageFilter(TradeDateFrom: from, TradeDateTo: to), x => x.Trade.Trade.TradeDate >= from && x.Trade.Trade.TradeDate <= to),
            (new TradePageFilter(OpenAtEnd: true), x => x.Open),
            (new TradePageFilter(OpenAtEnd: false, Side: Side.Buy, ExitReason: ExitReason.Stop), x => !x.Open && x.Trade.Trade.Side == Side.Buy && x.Trade.Trade.ExitReason == ExitReason.Stop),
        };

        foreach (var (filter, predicate) in cases)
        {
            var expected = all.Where(predicate).Select(x => x.Item1).ToList();
            var paged = await AllPagesAsync(run.RunKey, filter, 7);

            SameTrade.ShouldBeSame(paged, expected, filter.ToString()!);
        }

        (await AllPagesAsync(run.RunKey, new TradePageFilter(), 1000)).Count.ShouldBe(all.Count);
    }

    [Fact]
    public async Task A_damaged_row_with_an_unknown_enum_name_is_unreadable_and_never_a_wrong_value()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_unreadable_probe", Ct);
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo(), fixtureEdges: false);
        (await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "ALTER TABLE research.sim_trades DROP CONSTRAINT ck_sim_trades_side", Ct);
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "UPDATE research.sim_trades SET side = 'sideways' WHERE open_at_end = false AND ordinal = 3", Ct);

        var page = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(10), Ct).AsTask());
        var streamed = await Should.ThrowAsync<StoreReadException>(() => StoreKit.WithStoreAsync(scratch.Host.Services, async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct)));

        page.IsFailure.ShouldBeTrue();
        page.Error.Code.ShouldBe("store.row_unreadable");
        streamed.Message.ShouldContain("sideways");
        var untouched = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.PageTradesAsync(run.RunKey, new TradePageFilter(OpenAtEnd: true), new PageRequest(10), Ct).AsTask());
        untouched.IsSuccess.ShouldBeTrue();
    }

    // criterion 12: the same-entries shape

    [Fact]
    public async Task Two_runs_that_differ_only_in_the_window_share_their_entries_by_trade_key_and_the_join_uses_the_unique_index()
    {
        var a = await StoredAsync(fixtureEdges: false);
        var b = await StoredAsync(fixtureEdges: false);
        a.RunKey.ShouldNotBe(b.RunKey);
        var expected = a.Trades.Concat(a.OpenAtEnd).Select(t => t.Trade.Key).Intersect(b.Trades.Concat(b.OpenAtEnd).Select(t => t.Trade.Key)).Count();
        var ids = new List<long>();
        foreach (var run in new[] { a, b })
        {
            ids.Add(await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT id FROM research.sim_runs WHERE run_key = @k", Ct, ("k", run.RunKey)));
        }

        var shared = await StoreKit.CountAsync(
            factory.AdminConnectionString,
            "SELECT count(*) FROM research.sim_trades x JOIN research.sim_trades y ON y.trade_key = x.trade_key WHERE x.run_id = @a AND y.run_id = @b",
            Ct,
            ("a", ids[0]),
            ("b", ids[1]));

        shared.ShouldBe(expected);
        expected.ShouldBe(a.Trades.Count + a.OpenAtEnd.Count);
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var batch = new NpgsqlCommand("SET enable_seqscan = off; EXPLAIN SELECT 1 FROM research.sim_trades WHERE run_id = @a AND trade_key = @key", connection);
        batch.Parameters.AddWithValue("a", ids[0]);
        batch.Parameters.AddWithValue("key", a.Trades[0].Trade.Key);
        await using var reader = await batch.ExecuteReaderAsync(Ct);
        var plan = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            plan.Add(reader.GetString(0));
        }

        plan.ShouldNotContain(line => line.Contains("Seq Scan", StringComparison.Ordinal), string.Join("\n", plan));
        plan.ShouldContain(line => line.Contains("uq_sim_trades_run_trade_key", StringComparison.Ordinal) || line.Contains("pk_sim_trades", StringComparison.Ordinal), string.Join("\n", plan));
    }

    // criterion 18b: injection

    [Theory]
    [InlineData("'; drop table research.sim_trades; --")]
    [InlineData("%")]
    [InlineData("_")]
    public async Task A_symbol_that_is_a_statement_or_a_wildcard_matches_nothing_and_the_tables_are_intact(string symbol)
    {
        var run = await StoredAsync(fixtureEdges: false);

        var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(Symbol: symbol), new PageRequest(50), Ct).AsTask());

        page.Value.Items.ShouldBeEmpty();
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_trades WHERE run_id = (SELECT id FROM research.sim_runs WHERE run_key = @k)", Ct, ("k", run.RunKey))).ShouldBe(run.Trades.Count + run.OpenAtEnd.Count);
    }

    [Fact]
    public async Task A_symbol_of_5000_characters_matches_nothing_and_is_not_an_error()
    {
        var run = await StoredAsync(fixtureEdges: false);

        var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(Symbol: new string('x', 5000)), new PageRequest(50), Ct).AsTask());

        page.Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_key_that_is_a_statement_finds_nothing()
    {
        (await WithStore(s => s.FindAsync("x'; drop table research.sim_runs; --", Ct).AsTask())).ShouldBeNull();
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_runs", Ct)).ShouldBeGreaterThanOrEqualTo(0);
    }
}
