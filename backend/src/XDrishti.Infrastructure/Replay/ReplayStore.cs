using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Replay;

/// <summary>
/// <c>research.replay_runs</c>, <c>research.replay_sessions</c> and <c>research.replay_trades</c> behind <see cref="IReplayStore"/> and <see cref="IReplayReads"/> (spec SR-01 part B, ADR 0272): raw SQL, the only code that names these tables. Insert-only except five columns of a run
/// (state, cursor, result hash, end time, failure reason); nothing here deletes. The insert takes an advisory lock so that two requests cannot both see "no replay is active". A session and its trades are stored in one transaction with the move of the cursor, so a kill leaves either all of it or none.
/// </summary>
internal sealed class ReplayStore(NpgsqlDataSource dataSource) : IReplayStore, IReplayReads
{
    private const string RunSelect = """
        SELECT r.id, r.pair_id, r.range_from, r.range_to, r.mode, r.params::text, r.settings_snapshot::text, r.versions::text, r.learning_sources, r.input_hash, r.scope_hash, r.result_hash, r.state, r.cursor_session,
               (SELECT count(*)::int FROM research.replay_sessions s WHERE s.run_id = r.id), r.sessions_total,
               (SELECT count(*)::int FROM research.replay_runs o WHERE o.range_from = r.range_from AND o.range_to = r.range_to AND (o.created_at, o.id) <= (r.created_at, r.id)),
               r.created_at, r.finished_at, r.fail_reason, l.equity, l.drawdown
        FROM research.replay_runs r
        LEFT JOIN LATERAL (SELECT s.equity, s.drawdown FROM research.replay_sessions s WHERE s.run_id = r.id AND s.session = r.cursor_session) l ON true
        """;

    private const string TradeInsert = """
        INSERT INTO research.replay_trades (run_id, trade_no, symbol, side, cell_key, qty, entry_session, entry_at, entry_price, stop_price, swing, trade_key, exit_session, exit_at, gross_pnl, charges, net_pnl, charge_sheet_id, r_sim)
        VALUES (@run, @no, @symbol, @side, @cell, @qty, @entry_session, @entry_at, @entry_price, @stop, @swing, @key, @exit_session, @exit_at, @gross, @charges, @net, @sheet, @r)
        ON CONFLICT (run_id, trade_no) DO NOTHING
        """;

    public async ValueTask<ReplayInsert> InsertAsync(Guid? pairId, IReadOnlyList<NewReplayRun> runs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('research.replay_runs', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        // a start that died between its insert and its ledger rows left a queued, unrecorded row: past five minutes it is cancelled here, so it cannot refuse every later start
        await using (var stale = new NpgsqlCommand("UPDATE research.replay_runs SET state = 'cancelled', finished_at = now() WHERE state = 'queued' AND NOT recorded AND created_at < now() - interval '5 minutes'", connection, transaction))
        {
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        var active = await ReadRunsAsync(connection, transaction, RunSelect + " WHERE r.state IN ('queued', 'running', 'cancelling') ORDER BY r.created_at, r.id", null, cancellationToken);
        if (active.Count > 0)
        {
            return new ReplayInsert(false, active);
        }

        foreach (var run in runs)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO research.replay_runs (id, pair_id, range_from, range_to, mode, params, settings_snapshot, versions, learning_sources, input_hash, scope_hash, state, sessions_total, created_at)
                VALUES (@id, @pair, @from, @to, @mode, @params, @settings, @versions, @learning, @input, @scope, 'queued', @total, @at)
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, run.Id);
            insert.Parameters.AddWithValue("pair", NpgsqlDbType.Uuid, (object?)pairId ?? DBNull.Value);
            insert.Parameters.AddWithValue("from", NpgsqlDbType.Date, run.From);
            insert.Parameters.AddWithValue("to", NpgsqlDbType.Date, run.To);
            insert.Parameters.AddWithValue("mode", NpgsqlDbType.Varchar, run.Mode);
            insert.Parameters.AddWithValue("params", NpgsqlDbType.Jsonb, run.ParamsJson);
            insert.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, run.SettingsJson);
            insert.Parameters.AddWithValue("versions", NpgsqlDbType.Jsonb, run.VersionsJson);
            insert.Parameters.AddWithValue("learning", NpgsqlDbType.Text, (object?)run.LearningSources ?? DBNull.Value);
            insert.Parameters.AddWithValue("input", NpgsqlDbType.Char, run.InputHash);
            insert.Parameters.AddWithValue("scope", NpgsqlDbType.Char, run.ScopeHash);
            insert.Parameters.AddWithValue("total", NpgsqlDbType.Integer, run.SessionsTotal);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, run.CreatedAt.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var stored = await ReadRunsAsync(connection, transaction, RunSelect + " WHERE r.id = ANY(@ids) ORDER BY r.created_at, r.id", c => c.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, runs.Select(r => r.Id).ToArray()), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReplayInsert(true, stored);
    }

    public async ValueTask MarkRecordedAsync(IReadOnlyList<Guid> runIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        await using var command = dataSource.CreateCommand("UPDATE research.replay_runs SET recorded = true WHERE id = ANY(@ids) AND state = 'queued'");
        command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, runIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<ReplayRunRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var claim = new NpgsqlCommand("""
            UPDATE research.replay_runs SET state = 'running'
            WHERE id = (SELECT k.id FROM research.replay_runs k
                        WHERE k.state = 'queued' AND k.recorded AND NOT EXISTS (SELECT 1 FROM research.replay_runs a WHERE a.state IN ('running', 'cancelling'))
                        ORDER BY k.created_at, k.id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING id
            """, connection);
        if (await claim.ExecuteScalarAsync(cancellationToken) is not Guid id)
        {
            return null;
        }

        return OneOf(await ReadRunsAsync(connection, null, RunSelect + " WHERE r.id = @id", c => c.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id), cancellationToken));
    }

    public async ValueTask<int> RecoverAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH requeued AS (UPDATE research.replay_runs SET state = 'queued' WHERE state = 'running' RETURNING 1),
                 stopped AS (UPDATE research.replay_runs SET state = 'cancelled', finished_at = @now WHERE state = 'cancelling' OR (state = 'queued' AND NOT recorded AND created_at < @now - interval '5 minutes') RETURNING 1)
            SELECT (SELECT count(*) FROM requeued)::int + (SELECT count(*) FROM stopped)::int
            """, connection);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask<bool> SaveSessionAsync(Guid runId, ReplaySessionRow session, IReadOnlyList<ReplayTradeRow> trades, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(trades);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int inserted;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO research.replay_sessions (run_id, session, record, equity, drawdown, record_sha256)
            SELECT @run, @session, @record, @equity, @drawdown, @sha
            WHERE EXISTS (SELECT 1 FROM research.replay_runs WHERE id = @run AND state IN ('running', 'cancelling'))
            ON CONFLICT (run_id, session) DO NOTHING
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
            insert.Parameters.AddWithValue("session", NpgsqlDbType.Date, session.Session);
            insert.Parameters.AddWithValue("record", NpgsqlDbType.Jsonb, session.RecordJson);
            insert.Parameters.AddWithValue("equity", NpgsqlDbType.Numeric, session.Equity);
            insert.Parameters.AddWithValue("drawdown", NpgsqlDbType.Numeric, (object?)session.Drawdown ?? DBNull.Value);
            insert.Parameters.AddWithValue("sha", NpgsqlDbType.Char, session.RecordSha256);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inserted == 0)
        {
            return false; // stored already (idempotent by run and session), or the run is no longer active: nothing else is written
        }

        await WriteTradesAsync(connection, transaction, runId, trades, cancellationToken);
        await using (var cursor = new NpgsqlCommand("UPDATE research.replay_runs SET cursor_session = @session WHERE id = @run AND state IN ('running', 'cancelling') AND (cursor_session IS NULL OR cursor_session < @session)", connection, transaction))
        {
            cursor.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, runId);
            cursor.Parameters.AddWithValue("session", NpgsqlDbType.Date, session.Session);
            await cursor.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<string?> StateAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT state FROM research.replay_runs WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async ValueTask<IReadOnlyDictionary<DateOnly, string>> SessionHashesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT session, record_sha256 FROM research.replay_sessions WHERE run_id = @id ORDER BY session");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        var result = new Dictionary<DateOnly, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[DateOnly.FromDateTime(reader.GetDateTime(0))] = reader.GetString(1);
        }

        return result;
    }

    public async ValueTask<bool> FinishAsync(Guid runId, string state, string? resultHash, string? failReason, IReadOnlyList<ReplayTradeRow> openTrades, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openTrades);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        int changed;
        await using (var update = new NpgsqlCommand("UPDATE research.replay_runs SET state = @state, result_hash = @hash, fail_reason = @reason, finished_at = @now WHERE id = @id AND state IN ('running', 'cancelling')", connection, transaction))
        {
            update.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
            update.Parameters.AddWithValue("state", NpgsqlDbType.Varchar, state);
            update.Parameters.AddWithValue("hash", NpgsqlDbType.Char, (object?)resultHash ?? DBNull.Value);
            update.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, failReason is null ? DBNull.Value : failReason.Length <= 400 ? failReason : failReason[..400]);
            update.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            changed = await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (changed == 0)
        {
            return false;
        }

        await WriteTradesAsync(connection, transaction, runId, openTrades, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<string?> CancelAsync(Guid runId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE research.replay_runs SET state = CASE state WHEN 'queued' THEN 'cancelled' ELSE 'cancelling' END, finished_at = CASE state WHEN 'queued' THEN @now ELSE NULL END
            WHERE id = @id AND state IN ('queued', 'running', 'cancelling')
            RETURNING state
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async ValueTask<bool> AnyRunningAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM research.replay_runs WHERE state IN ('running', 'cancelling'))");
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask<ReplayRunRow?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return OneOf(await ReadRunsAsync(connection, null, RunSelect + " WHERE r.id = @id", c => c.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId), cancellationToken));
    }

    public async ValueTask<IReadOnlyList<ReplayRunRow>> ListRunsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadRunsAsync(connection, null, RunSelect + " ORDER BY r.created_at DESC, r.id OFFSET @skip LIMIT @take", c =>
        {
            c.Parameters.AddWithValue("skip", NpgsqlDbType.Integer, Math.Max(0, skip));
            c.Parameters.AddWithValue("take", NpgsqlDbType.Integer, Math.Clamp(take, 1, 200));
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ReplayRunRow>> GetPairAsync(Guid pairId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadRunsAsync(connection, null, RunSelect + " WHERE r.pair_id = @pair ORDER BY r.created_at, r.id", c => c.Parameters.AddWithValue("pair", NpgsqlDbType.Uuid, pairId), cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ReplaySessionRow>> SessionsAsync(Guid runId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT session, record::text, equity, drawdown, record_sha256 FROM research.replay_sessions
            WHERE run_id = @id AND (@from::date IS NULL OR session >= @from) AND (@to::date IS NULL OR session <= @to) ORDER BY session
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, (object?)from ?? DBNull.Value);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, (object?)to ?? DBNull.Value);
        var rows = new List<ReplaySessionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadSession(reader));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<ReplayEquityRow>> EquityAsync(Guid runId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT session, equity, drawdown, COALESCE((record->>'openPositions')::int, 0) FROM research.replay_sessions
            WHERE run_id = @id AND (@from::date IS NULL OR session >= @from) AND (@to::date IS NULL OR session <= @to) ORDER BY session
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, (object?)from ?? DBNull.Value);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, (object?)to ?? DBNull.Value);
        var rows = new List<ReplayEquityRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ReplayEquityRow(Day(reader, 0)!.Value, reader.GetDecimal(1), Money(reader, 2), reader.GetInt32(3)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<string>> LabelsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT l FROM research.replay_sessions s, jsonb_array_elements_text(s.record->'labels') AS l WHERE s.run_id = @id ORDER BY l");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    public async ValueTask<ReplaySessionRow?> SessionAsync(Guid runId, DateOnly session, CancellationToken cancellationToken) =>
        OneOf(await SessionsAsync(runId, session, session, cancellationToken));

    public async ValueTask<IReadOnlyList<ReplayTradeRow>> TradesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT trade_no, symbol, side, cell_key, qty, entry_session, entry_at, entry_price, stop_price, swing, trade_key, exit_session, exit_at, gross_pnl, charges, net_pnl, charge_sheet_id, r_sim
            FROM research.replay_trades WHERE run_id = @id ORDER BY trade_no
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, runId);
        var rows = new List<ReplayTradeRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ReplayTradeRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), Day(reader, 5)!.Value, reader.GetFieldValue<DateTimeOffset>(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetBoolean(9), reader.GetString(10),
                Day(reader, 11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12), Money(reader, 13), Money(reader, 14), Money(reader, 15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetDouble(17)));
        }

        return rows;
    }

    public async ValueTask<ReplaySummary?> SummaryAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await GetRunAsync(runId, cancellationToken) is not { CursorSession: { } asOf } run)
        {
            return null;
        }

        var capital = JsonDocument.Parse(run.ParamsJson).RootElement.GetProperty("capital").GetDecimal();
        var trades = await TradesAsync(runId, cancellationToken);
        var book = trades.Where(t => t.EntrySession <= asOf).Select(t => new LedgerTrade(
            t.TradeNo, Guid.Empty, t.Symbol, t.Side, t.Qty, t.EntryAt, t.EntryPrice, t.StopPrice, t.Swing, "primary", t.TradeKey,
            t.ExitAt is { } at && t.ExitSession <= asOf ? new LedgerExit(at, t.GrossPnl ?? 0m, t.Charges, t.NetPnl, t.ChargeSheetId, t.RSim ?? 0) : null)).ToList();
        return new ReplaySummary(run, PaperLedger.Compute(capital, book, asOf), trades.Count);
    }

    private static async Task WriteTradesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, IReadOnlyList<ReplayTradeRow> trades, CancellationToken cancellationToken)
    {
        foreach (var t in trades)
        {
            await using var command = new NpgsqlCommand(TradeInsert, connection, transaction);
            var p = command.Parameters;
            p.AddWithValue("run", NpgsqlDbType.Uuid, runId);
            p.AddWithValue("no", NpgsqlDbType.Bigint, t.TradeNo);
            p.AddWithValue("symbol", NpgsqlDbType.Varchar, t.Symbol);
            p.AddWithValue("side", NpgsqlDbType.Varchar, t.Side);
            p.AddWithValue("cell", NpgsqlDbType.Text, t.CellKey);
            p.AddWithValue("qty", NpgsqlDbType.Integer, t.Qty);
            p.AddWithValue("entry_session", NpgsqlDbType.Date, t.EntrySession);
            p.AddWithValue("entry_at", NpgsqlDbType.TimestampTz, t.EntryAt.ToUniversalTime());
            p.AddWithValue("entry_price", NpgsqlDbType.Numeric, t.EntryPrice);
            p.AddWithValue("stop", NpgsqlDbType.Numeric, t.StopPrice);
            p.AddWithValue("swing", NpgsqlDbType.Boolean, t.Swing);
            p.AddWithValue("key", NpgsqlDbType.Text, t.TradeKey);
            p.AddWithValue("exit_session", NpgsqlDbType.Date, (object?)t.ExitSession ?? DBNull.Value);
            p.AddWithValue("exit_at", NpgsqlDbType.TimestampTz, t.ExitAt is { } at ? at.ToUniversalTime() : DBNull.Value);
            p.AddWithValue("gross", NpgsqlDbType.Numeric, (object?)t.GrossPnl ?? DBNull.Value);
            p.AddWithValue("charges", NpgsqlDbType.Numeric, (object?)t.Charges ?? DBNull.Value);
            p.AddWithValue("net", NpgsqlDbType.Numeric, (object?)t.NetPnl ?? DBNull.Value);
            p.AddWithValue("sheet", NpgsqlDbType.Varchar, (object?)t.ChargeSheetId ?? DBNull.Value);
            p.AddWithValue("r", NpgsqlDbType.Double, (object?)t.RSim ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<List<ReplayRunRow>> ReadRunsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, Action<NpgsqlCommand>? bind, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        bind?.Invoke(command);
        var rows = new List<ReplayRunRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ReplayRunRow(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), Day(reader, 2)!.Value, Day(reader, 3)!.Value, reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetString(12), Day(reader, 13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), reader.GetFieldValue<DateTimeOffset>(17),
                reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18), reader.IsDBNull(19) ? null : reader.GetString(19), Money(reader, 20), Money(reader, 21)));
        }

        return rows;
    }

    private static ReplaySessionRow ReadSession(NpgsqlDataReader reader) =>
        new(Day(reader, 0)!.Value, reader.GetString(1), reader.GetDecimal(2), Money(reader, 3), reader.GetString(4));

    private static T? OneOf<T>(IReadOnlyList<T> rows) where T : class => rows.Count == 0 ? null : rows[0];

    private static DateOnly? Day(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    private static decimal? Money(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
