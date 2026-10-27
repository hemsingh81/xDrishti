using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Infrastructure.Ai.Evaluation;

/// <summary>The outcome of seeding.</summary>
public enum SeedOutcome
{
    /// <summary>The fixture was written.</summary>
    Seeded,

    /// <summary>The database already holds this fixture (its marker matches): nothing was changed.</summary>
    Unchanged,
}

/// <summary>Why seeding was refused.</summary>
public sealed class SeedRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// Writes the eval fixture into an empty, migrated database (spec P9-09 AC 1, 13, ADR 0143): synthetic instruments, baskets, two stored runs, one plan, two holdings, a statistics cell, lessons and hypotheses, the database's own assistant settings (on, portfolio context on) and, last, the marker
/// with the hash of the fixture file. SAFETY: it refuses unless the database is the required one (the verb names <c>xdrishti_eval</c>) and holds none of the tables' rows, and it runs as the owner in one transaction, so it can never mix the fixture into real data and never leaves half a fixture.
/// Seeding twice changes nothing. It is a developer act (<c>scripts/eval-db.sh</c>); no service, endpoint or job calls it.
/// </summary>
public static class FixtureSeeder
{
    /// <summary>The tables that must be empty before the first seeding (one row anywhere is real data).</summary>
    private static readonly string[] MustBeEmpty =
    [
        "research.sim_runs", "research.cell_stats", "research.lessons", "research.hypotheses", "market.instruments", "market.baskets", "portfolio.holdings", "trading.plans", "accounts.broker_accounts", "accounts.portfolio_buckets", "ai.settings",
    ];

    public static async Task<SeedOutcome> SeedAsync(string ownerConnectionString, EvalFixture fixture, string fixtureText, string requiredDatabase, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerConnectionString);
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(requiredDatabase);
        var hash = EvalFixture.HashOf(fixtureText);
        await using var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        var database = (string)(await Scalar(connection, null, "SELECT current_database()", cancellationToken))!;
        if (!string.Equals(database, requiredDatabase, StringComparison.Ordinal))
        {
            throw new SeedRefusedException($"The fixture is seeded only into the database '{requiredDatabase}'; this connection is '{database}'.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await Exec(connection, transaction, "LOCK TABLE ai.eval_marker IN EXCLUSIVE MODE", cancellationToken);
        if (await Scalar(connection, transaction, "SELECT fixture_hash FROM ai.eval_marker WHERE id = 1", cancellationToken) is string existing)
        {
            if (string.Equals(existing.Trim(), hash, StringComparison.Ordinal))
            {
                return SeedOutcome.Unchanged;
            }

            throw new SeedRefusedException("The database holds another version of the fixture; create the database again.");
        }

        foreach (var table in MustBeEmpty)
        {
            if ((long)(await Scalar(connection, transaction, $"SELECT count(*) FROM {table}", cancellationToken))! > 0)
            {
                throw new SeedRefusedException($"The table {table} is not empty; the fixture is seeded only into a fresh database.");
            }
        }

        await WriteAsync(connection, transaction, fixture, cancellationToken);
        await Exec(connection, transaction, "INSERT INTO ai.eval_marker (id, fixture_hash) VALUES (1, @hash)", cancellationToken, ("hash", NpgsqlDbType.Char, hash));
        await transaction.CommitAsync(cancellationToken);
        return SeedOutcome.Seeded;
    }

    private static async Task WriteAsync(NpgsqlConnection c, NpgsqlTransaction t, EvalFixture f, CancellationToken ct)
    {
        var instruments = f.Instruments.ToDictionary(i => i.Symbol, i => Id("instrument", i.Symbol), StringComparer.Ordinal);
        var baskets = f.Baskets.ToDictionary(b => b.Name, b => Id("basket", b.Name), StringComparer.Ordinal);
        var stamp = f.FixtureDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

        // The assistant's own switches in this database: the tools answer, and the portfolio views return rows (spec AC 13).
        await Exec(c, t, "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, true, 6, 'owner:eval-fixture@example.test', 'eval fixture database')", ct);

        for (var i = 0; i < f.Instruments.Count; i++)
        {
            var instrument = f.Instruments[i];
            await Exec(
                c, t,
                """
                INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, sector, fno_eligible, first_seen_at, last_seen_at)
                VALUES (@id, @symbol, 'NSE', 'NSE_EQ', 'share', @name, @symbol, @isin, 'EQ', 1, @symbol, 'Eval', false, @stamp, @stamp)
                """,
                ct, ("id", NpgsqlDbType.Uuid, instruments[instrument.Symbol]), ("symbol", NpgsqlDbType.Varchar, instrument.Symbol), ("name", NpgsqlDbType.Varchar, instrument.Name), ("isin", NpgsqlDbType.Varchar, $"XS{i + 1:D10}"), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        }

        for (var i = 0; i < f.Baskets.Count; i++)
        {
            var basket = f.Baskets[i];
            var purpose = basket.Name == f.Portfolio.Basket ? "long_term" : "trading";
            await Exec(
                c, t,
                "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision) VALUES (@id, @name, '#336699', @purpose, 'synthetic basket of the eval fixture', @position, @stamp, 1)",
                ct, ("id", NpgsqlDbType.Uuid, baskets[basket.Name]), ("name", NpgsqlDbType.Varchar, basket.Name), ("purpose", NpgsqlDbType.Varchar, purpose), ("position", NpgsqlDbType.Integer, i), ("stamp", NpgsqlDbType.TimestampTz, stamp.AddDays(-90)));
            for (var m = 0; m < basket.Symbols.Count; m++)
            {
                await Exec(
                    c, t,
                    "INSERT INTO market.basket_members (id, basket_id, instrument_id, position, valid_from, added_by) VALUES (@id, @basket, @instrument, @position, @from, 'eval-fixture')",
                    ct, ("id", NpgsqlDbType.Uuid, Id("member", basket.Name + "|" + basket.Symbols[m])), ("basket", NpgsqlDbType.Uuid, baskets[basket.Name]), ("instrument", NpgsqlDbType.Uuid, instruments[basket.Symbols[m]]), ("position", NpgsqlDbType.Integer, m), ("from", NpgsqlDbType.TimestampTz, stamp.AddDays(-90)));
            }
        }

        string? orbRunKey = null;
        foreach (var run in f.Runs)
        {
            await Exec(c, t, "INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES (@id, 'intraday', true, @stamp) ON CONFLICT DO NOTHING", ct, ("id", NpgsqlDbType.Varchar, run.Strategy), ("stamp", NpgsqlDbType.TimestampTz, stamp));
            var runKey = Sha("eval-run|" + run.Key);
            orbRunKey ??= runKey;
            await SeedRunAsync(c, t, run, runKey, instruments, baskets, f, stamp, ct);
        }

        await SeedPlanAsync(c, t, f, stamp, ct);
        await SeedPortfolioAsync(c, t, f, instruments, stamp, ct);
        await SeedCellAsync(c, t, f, stamp, ct);
        await SeedLearningAsync(c, t, f, orbRunKey!, stamp, ct);
    }

    private static async Task SeedRunAsync(NpgsqlConnection c, NpgsqlTransaction t, FixtureRun run, string runKey, Dictionary<string, Guid> instruments, Dictionary<string, Guid> baskets, EvalFixture f, DateTime stamp, CancellationToken ct)
    {
        var count = run.Trades.Count;
        await Exec(
            c, t,
            """
            INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label, instruments, instruments_run,
                trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, record_json, record_sha256, trades_sha256, created_at, created_by)
            VALUES (@key, 'sim-5', 5, 4, @strategy, @def, @from, @to, 'basket', @universe, 6, 6, @n, @n, @n, 0, false, 'modelled', '{}', @record, @trades, @stamp, 'eval-fixture')
            """,
            ct, ("key", NpgsqlDbType.Char, runKey), ("strategy", NpgsqlDbType.Varchar, run.Strategy), ("def", NpgsqlDbType.Char, Sha("eval-definition|" + run.Strategy)), ("from", NpgsqlDbType.Date, run.WindowStart), ("to", NpgsqlDbType.Date, run.WindowEnd),
            ("universe", NpgsqlDbType.Varchar, run.Universe), ("n", NpgsqlDbType.Integer, count), ("record", NpgsqlDbType.Char, Sha("eval-record|" + run.Key)), ("trades", NpgsqlDbType.Char, Sha("eval-trades|" + run.Key)), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        var runId = Convert.ToInt64(await Scalar(c, t, "SELECT id FROM research.sim_runs WHERE run_key = @key", ct, ("key", NpgsqlDbType.Char, runKey)), CultureInfo.InvariantCulture);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var trade = run.Trades[ordinal];
            var buy = trade.Side == "BUY";
            var entry = new DateTimeOffset(trade.Entry.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
            var exit = entry + 3600;
            var r = (decimal)trade.R;
            var exitPrice = buy ? 100m + r : 100m - r;
            var member = f.Baskets.Where(b => b.Symbols.Contains(trade.Symbol) && b.Name != f.Portfolio.Basket).Select(b => baskets[b.Name]).ToArray();
            await Exec(
                c, t,
                """
                INSERT INTO research.sim_trades (run_id, open_at_end, ordinal, trade_key, basket_ids, costs, decision_epoch_seconds, entry_epoch_seconds, entry_fill, entry_filled_price, entry_price, exit_epoch_seconds, exit_filled_price, exit_price, exit_reason, fills,
                    instrument_id, mae_r, mfe_r, r_exact, r_filled, sessions_held, sessions_unseen, side, slippage_r, stop_price, strategy_id, symbol, tick_fallback, trade_date, trigger_bar_close, trigger_bar_complete, trigger_bar_epoch,
                    trigger_bar_high, trigger_bar_low, trigger_bar_minutes, trigger_bar_open, trigger_bar_volume, trigger_timeframe)
                VALUES (@run, false, @ordinal, @key, @baskets, 'none', @entry, @entry, 'atopen', 100, 100, @exit, @exitPrice, @exitPrice, @reason, 'modelled',
                    @instrument, 0.1, @mfe, @r, @r, 1, 0, @side, 0, @stop, @strategy, @symbol, false, @date, 100, true, @entry,
                    101, 99, 15, 100, 1000, 'm15')
                """,
                ct, ("run", NpgsqlDbType.Bigint, runId), ("ordinal", NpgsqlDbType.Integer, ordinal), ("key", NpgsqlDbType.Varchar, $"{run.Strategy}|{trade.Symbol}|{trade.Side}|{trade.Entry:yyyy-MM-dd}"), ("baskets", NpgsqlDbType.Array | NpgsqlDbType.Uuid, member),
                ("entry", NpgsqlDbType.Bigint, entry), ("exit", NpgsqlDbType.Bigint, exit), ("exitPrice", NpgsqlDbType.Numeric, exitPrice), ("reason", NpgsqlDbType.Varchar, r > 0 ? "target" : r < 0 ? "stop" : "squareoff"),
                ("instrument", NpgsqlDbType.Uuid, instruments[trade.Symbol]), ("mfe", NpgsqlDbType.Numeric, Math.Max(r, 0.1m)), ("r", NpgsqlDbType.Numeric, r), ("side", NpgsqlDbType.Varchar, buy ? "buy" : "sell"), ("stop", NpgsqlDbType.Numeric, buy ? 99m : 101m),
                ("strategy", NpgsqlDbType.Varchar, run.Strategy), ("symbol", NpgsqlDbType.Varchar, trade.Symbol), ("date", NpgsqlDbType.Date, trade.Entry));
        }
    }

    private static async Task SeedPlanAsync(NpgsqlConnection c, NpgsqlTransaction t, EvalFixture f, DateTime stamp, CancellationToken ct)
    {
        var plan = f.Plan;
        var accountId = Id("account", plan.PaperAccount);
        await Exec(
            c, t,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, @slug, @name, 'dhan', @client, '#000000', @stamp, @stamp, false, false, false, 0)
            """,
            ct, ("id", NpgsqlDbType.Uuid, accountId), ("slug", NpgsqlDbType.Varchar, "eval-paper"), ("name", NpgsqlDbType.Varchar, plan.PaperAccount), ("client", NpgsqlDbType.Varchar, "9900000001"), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        var planId = (long)(await Scalar(
            c, t,
            """
            INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at)
            VALUES (@key, @date, @cutoff, 1, @hash, 'stored cells, in-sample', false, 'a synthetic plan of the eval fixture', 'in-sample only', 'eval-fixture', @stamp)
            RETURNING id
            """,
            ct, ("key", NpgsqlDbType.Varchar, "eval-plan-" + plan.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("date", NpgsqlDbType.Date, plan.Date), ("cutoff", NpgsqlDbType.TimestampTz, plan.Date.ToDateTime(new TimeOnly(3, 0), DateTimeKind.Utc)),
            ("hash", NpgsqlDbType.Char, Sha("eval-plan|" + plan.Date)), ("stamp", NpgsqlDbType.TimestampTz, stamp)))!;
        foreach (var ticket in plan.Primary)
        {
            var cell = $"{ticket.Strategy}|{ticket.Timeframe}|{ticket.Side}|INTRADAY|*";
            var first = ticket.Rank == 1;
            var itemId = (long)(await Scalar(
                c, t,
                """
                INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis,
                    p_win, conservative_expectancy_r, unavailable, p_win_lower, entry_ref_price, levels_session, reward_risk, stop_anchor, stop_level, target_level)
                VALUES (@plan, @rank, 'primary', @symbol, @side, @strategy, @hash, 'above the opening range', 'below the opening range low', 'at twice the risk', 'today', ARRAY[]::text[], @cell, @trades, '2026-01-05..2026-02-27', 'cell',
                    @pwin, @exp, ARRAY[]::text[], @lower, @entry, @session, @rr, @anchor, @stop, @target)
                RETURNING id
                """,
                ct, ("plan", NpgsqlDbType.Bigint, planId), ("rank", NpgsqlDbType.Integer, ticket.Rank), ("symbol", NpgsqlDbType.Varchar, ticket.Symbol), ("side", NpgsqlDbType.Varchar, ticket.Side), ("strategy", NpgsqlDbType.Varchar, ticket.Strategy),
                ("hash", NpgsqlDbType.Char, Sha("eval-definition|" + ticket.Strategy)), ("cell", NpgsqlDbType.Varchar, cell), ("trades", NpgsqlDbType.Integer, ticket.Strategy == "ORB" ? 12 : 16), ("pwin", NpgsqlDbType.Numeric, first ? 0.52m : 0.5m),
                ("exp", NpgsqlDbType.Numeric, first ? 0.05m : 0.02m), ("lower", NpgsqlDbType.Numeric, first ? 0.3m : (object)DBNull.Value), ("entry", NpgsqlDbType.Numeric, first ? 100m : (object)DBNull.Value),
                ("session", NpgsqlDbType.Date, first ? plan.Date : (object)DBNull.Value), ("rr", NpgsqlDbType.Numeric, first ? 2m : (object)DBNull.Value), ("anchor", NpgsqlDbType.Varchar, first ? "level" : (object)DBNull.Value),
                ("stop", NpgsqlDbType.Numeric, first ? 95m : (object)DBNull.Value), ("target", NpgsqlDbType.Numeric, first ? 110m : (object)DBNull.Value)))!;
            await Exec(c, t, "INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity) VALUES (@item, @account, 'paper', @quantity)", ct, ("item", NpgsqlDbType.Bigint, itemId), ("account", NpgsqlDbType.Uuid, accountId), ("quantity", NpgsqlDbType.Integer, ticket.AllocationShares));
        }

        var rank = plan.Primary.Count;
        foreach (var (symbol, group, reason) in plan.Reserve.Select(s => (s, "reserve", "next in rank when a primary ticket is dropped")).Concat(plan.Rejected.Select(r => (r.Symbol, "rejected", r.Reason))))
        {
            rank++;
            await Exec(
                c, t,
                """
                INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, p_win, conservative_expectancy_r, unavailable)
                VALUES (@plan, @rank, @group, @symbol, 'BUY', 'VWAP_PULLBACK', @hash, 'above vwap', 'below vwap', 'at twice the risk', 'today', ARRAY[@reason]::text[], 'VWAP_PULLBACK|15m|BUY|INTRADAY|*', 16, '2026-01-05..2026-02-27', 'cell', 0.5, 0.01, ARRAY[]::text[])
                """,
                ct, ("plan", NpgsqlDbType.Bigint, planId), ("rank", NpgsqlDbType.Integer, rank), ("group", NpgsqlDbType.Varchar, group), ("symbol", NpgsqlDbType.Varchar, symbol), ("hash", NpgsqlDbType.Char, Sha("eval-definition|VWAP_PULLBACK")), ("reason", NpgsqlDbType.Varchar, reason));
        }
    }

    private static async Task SeedPortfolioAsync(NpgsqlConnection c, NpgsqlTransaction t, EvalFixture f, Dictionary<string, Guid> instruments, DateTime stamp, CancellationToken ct)
    {
        var accountId = Id("account", f.Portfolio.Account);
        var bucketId = Id("bucket", "Eval long term");
        await Exec(
            c, t,
            """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES (@id, @slug, @name, 'dhan', @client, '#000000', @stamp, @stamp, false, false, false, 0)
            """,
            ct, ("id", NpgsqlDbType.Uuid, accountId), ("slug", NpgsqlDbType.Varchar, "eval-long-term"), ("name", NpgsqlDbType.Varchar, f.Portfolio.Account), ("client", NpgsqlDbType.Varchar, "9900000002"), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        await Exec(c, t, "INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES (@id, 'Eval long term', @stamp)", ct, ("id", NpgsqlDbType.Uuid, bucketId), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        foreach (var holding in f.Portfolio.Holdings)
        {
            var holdingId = Id("holding", holding.Symbol);
            await Exec(
                c, t,
                """
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, instrument_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, last_price_at, status, first_seen_at, synced_at)
                VALUES (@id, @account, @bucket, @instrument, 'NSE', @security, @symbol, @name, @qty, @cost, @last, @at, 'open', @stamp, @stamp)
                """,
                ct, ("id", NpgsqlDbType.Uuid, holdingId), ("account", NpgsqlDbType.Uuid, accountId), ("bucket", NpgsqlDbType.Uuid, bucketId), ("instrument", NpgsqlDbType.Uuid, instruments[holding.Symbol]), ("security", NpgsqlDbType.Varchar, "EVSEC-" + holding.Symbol),
                ("symbol", NpgsqlDbType.Varchar, holding.Symbol), ("name", NpgsqlDbType.Varchar, f.Instruments.First(i => i.Symbol == holding.Symbol).Name), ("qty", NpgsqlDbType.Bigint, (long)holding.Shares), ("cost", NpgsqlDbType.Numeric, holding.BuyPrice),
                ("last", NpgsqlDbType.Numeric, holding.LastPrice), ("at", NpgsqlDbType.TimestampTz, stamp), ("stamp", NpgsqlDbType.TimestampTz, stamp));
            await Exec(
                c, t,
                "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (@id, @holding, @on, @qty, @cost, 'import', false, @stamp, false)",
                ct, ("id", NpgsqlDbType.Uuid, Id("lot", holding.Symbol)), ("holding", NpgsqlDbType.Uuid, holdingId), ("on", NpgsqlDbType.Date, holding.BoughtOn), ("qty", NpgsqlDbType.Bigint, (long)holding.Shares), ("cost", NpgsqlDbType.Numeric, holding.BuyPrice), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        }
    }

    private static async Task SeedCellAsync(NpgsqlConnection c, NpgsqlTransaction t, EvalFixture f, DateTime stamp, CancellationToken ct)
    {
        var parts = f.CellStats.Cell.Split('|');
        foreach (var window in f.CellStats.Windows)
        {
            await Exec(
                c, t,
                """
                INSERT INTO research.cell_stats (snapshot_key, as_of, cell_key, strategy, timeframe, side, style, exit_policy, regime, window_code, costed, n, wins, effective_n, win_rate_raw, prior_rate, win_rate_posterior, win_rate_lower, win_rate_upper,
                    avg_win_r, avg_loss_r, expectancy_r, profit_factor, max_drawdown_r, enough, half_life_days, prior_strength, min_trades, credible_level, sources, created_at, created_by)
                VALUES (@snapshot, @asof, @cell, @strategy, @timeframe, @side, 'intraday', 'fixed', '*', @window, false, @n, 5, @n, 0.4167, 0.5, 0.45, 0.25, 0.65, 1.7, -1.0, 0.125, 1.2143, 3.0, false, 60, 10, 30, 0.9, 'sim', @stamp, 'eval-fixture')
                """,
                ct, ("snapshot", NpgsqlDbType.Char, Sha("eval-cell|" + f.CellStats.Cell)), ("asof", NpgsqlDbType.Date, f.FixtureDate), ("cell", NpgsqlDbType.Varchar, f.CellStats.Cell), ("strategy", NpgsqlDbType.Varchar, parts[0]), ("timeframe", NpgsqlDbType.Varchar, parts[1]),
                ("side", NpgsqlDbType.Varchar, parts[2]), ("window", NpgsqlDbType.Varchar, window.Window), ("n", NpgsqlDbType.Integer, window.N), ("stamp", NpgsqlDbType.TimestampTz, stamp));
        }
    }

    private static async Task SeedLearningAsync(NpgsqlConnection c, NpgsqlTransaction t, EvalFixture f, string baselineRunKey, DateTime stamp, CancellationToken ct)
    {
        // A lesson or hypothesis is a log: an opening row, and a row for every move. A move needs the id of the opening row.
        foreach (var lesson in f.Learning.Lessons)
        {
            var opening = (long)(await Scalar(
                c, t,
                "INSERT INTO research.lessons (state, from_state, monitor_kind, subject, evidence_key, problem, reason, actor, created_at) VALUES ('open', NULL, 'cusum', @subject, @key, 'a synthetic lesson of the eval fixture', 'opened by the fixture', 'eval-fixture', @stamp) RETURNING id",
                ct, ("subject", NpgsqlDbType.Varchar, "Eval lesson " + lesson.Key), ("key", NpgsqlDbType.Varchar, lesson.Key), ("stamp", NpgsqlDbType.TimestampTz, stamp)))!;
            if (lesson.Status != "open")
            {
                await Exec(
                    c, t,
                    "INSERT INTO research.lessons (lesson_id, state, from_state, monitor_kind, subject, evidence_key, problem, reason, actor, created_at) VALUES (@opening, @state, 'open', 'cusum', @subject, @key, 'a synthetic lesson of the eval fixture', 'moved by the fixture', 'eval-fixture', @stamp)",
                    ct, ("opening", NpgsqlDbType.Bigint, opening), ("state", NpgsqlDbType.Varchar, lesson.Status == "closed" ? "dismissed" : lesson.Status), ("subject", NpgsqlDbType.Varchar, "Eval lesson " + lesson.Key), ("key", NpgsqlDbType.Varchar, lesson.Key), ("stamp", NpgsqlDbType.TimestampTz, stamp.AddMinutes(1)));
            }
        }

        foreach (var hypothesis in f.Learning.Hypotheses)
        {
            var opening = (long)(await Scalar(
                c, t,
                """
                INSERT INTO research.hypotheses (state, from_state, source, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, reason, actor, created_at)
                VALUES ('proposed', NULL, 'owner', 'slippage_bps', 'Slippage', '1', '2', @rationale, @run, 'proposed by the fixture', 'eval-fixture', @stamp) RETURNING id
                """,
                ct, ("rationale", NpgsqlDbType.Varchar, "Eval hypothesis " + hypothesis.Key), ("run", NpgsqlDbType.Char, baselineRunKey), ("stamp", NpgsqlDbType.TimestampTz, stamp)))!;
            if (hypothesis.Status != "proposed")
            {
                await Exec(
                    c, t,
                    """
                    INSERT INTO research.hypotheses (hypothesis_id, state, from_state, source, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, reason, actor, created_at)
                    VALUES (@opening, @state, 'proposed', 'owner', 'slippage_bps', 'Slippage', '1', '2', @rationale, @run, 'moved by the fixture', 'eval-fixture', @stamp)
                    """,
                    ct, ("opening", NpgsqlDbType.Bigint, opening), ("state", NpgsqlDbType.Varchar, hypothesis.Status), ("rationale", NpgsqlDbType.Varchar, "Eval hypothesis " + hypothesis.Key), ("run", NpgsqlDbType.Char, baselineRunKey), ("stamp", NpgsqlDbType.TimestampTz, stamp.AddMinutes(1)));
            }
        }
    }

    private static async Task<object?> Scalar(NpgsqlConnection c, NpgsqlTransaction? t, string sql, CancellationToken ct, params (string Name, NpgsqlDbType Type, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, c, t);
        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.AddWithValue(name, type, value);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }

    private static async Task Exec(NpgsqlConnection c, NpgsqlTransaction? t, string sql, CancellationToken ct, params (string Name, NpgsqlDbType Type, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, c, t);
        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.AddWithValue(name, type, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>A stable id for a fixture record: the first 16 bytes of the SHA-256 of its kind and name, so seeding twice gives the same ids.</summary>
    private static Guid Id(string kind, string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"xdrishti-eval|{kind}|{name}"))[..16]);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
