using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Replay;

namespace XDrishti.Infrastructure.Replay;

/// <summary>
/// <c>research.sim_days_runs</c> and <c>research.sim_days_snapshots</c> behind <see cref="ISimDaysStore"/> (SR-04 part A, ADR 0293): raw SQL, the only code that names these tables. Every move is a compare-and-set on the phase (and the cursor); the partial unique index admits one active run, so
/// a second start or a Continue beside an active run is refused by the database. The advance inserts the snapshot and moves the cursor in one transaction. Nothing here deletes.
/// </summary>
internal sealed class SimDaysStore(NpgsqlDataSource dataSource) : ISimDaysStore
{
    private const string Select = """
        SELECT r.id, r.input_hash, r.params::text, r.settings_snapshot::text, r.versions::text, r.learning_sources, r.capital, r.mode, r.range_start, r.range_end, r.requested_end, r.cursor_session, r.phase, r.auto,
               r.read_through, r.holdout_note, r.fail_reason, r.pending_session, r.pending_record::text, r.pending_trades::text, r.pending_equity, r.pending_hash, r.pending_end,
               (SELECT count(*)::int FROM research.sim_days_snapshots s WHERE s.run_id = r.id),
               (SELECT s.equity_close FROM research.sim_days_snapshots s WHERE s.run_id = r.id ORDER BY s.session DESC LIMIT 1),
               r.created_at, r.updated_at, r.manual, r.waiting
        FROM research.sim_days_runs r
        """;

    private const string Active = "phase IN ('idle', 'settling', 'settled')";

    public async ValueTask<bool> InsertAsync(NewSimDaysRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO research.sim_days_runs (id, input_hash, params, settings_snapshot, versions, learning_sources, capital, mode, range_start, range_end, requested_end, cursor_session, phase, read_through, holdout_note, created_at, updated_at)
            VALUES (@id, @input, @params, @settings, @versions, @learning, @capital, @mode, @start, @end, @requested, @cursor, 'idle', @through, @note, @at, @at)
            ON CONFLICT DO NOTHING
            """);
        var p = command.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Uuid, run.Id);
        p.AddWithValue("input", NpgsqlDbType.Char, run.InputHash);
        p.AddWithValue("params", NpgsqlDbType.Jsonb, run.ParamsJson);
        p.AddWithValue("settings", NpgsqlDbType.Jsonb, run.SettingsJson);
        p.AddWithValue("versions", NpgsqlDbType.Jsonb, run.VersionsJson);
        p.AddWithValue("learning", NpgsqlDbType.Text, (object?)run.LearningSources ?? DBNull.Value);
        p.AddWithValue("capital", NpgsqlDbType.Numeric, run.Capital);
        p.AddWithValue("mode", NpgsqlDbType.Varchar, run.Mode);
        p.AddWithValue("start", NpgsqlDbType.Date, run.Start);
        p.AddWithValue("end", NpgsqlDbType.Date, run.End);
        p.AddWithValue("requested", NpgsqlDbType.Date, run.RequestedEnd);
        p.AddWithValue("cursor", NpgsqlDbType.Date, run.Cursor);
        p.AddWithValue("through", NpgsqlDbType.Date, (object?)run.ReadThrough ?? DBNull.Value);
        p.AddWithValue("note", NpgsqlDbType.Varchar, (object?)Cap(run.HoldOutNote, 1000) ?? DBNull.Value);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, run.CreatedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1; // the active index refuses a second active run
    }

    public async ValueTask<SimDaysRunRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await OneAsync(Select + " WHERE r.id = @id", c => c.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id), cancellationToken);

    public async ValueTask<SimDaysRunRow?> ActiveAsync(CancellationToken cancellationToken) =>
        await OneAsync(Select + " WHERE r." + Active, null, cancellationToken);

    public async ValueTask<SimDaysRunRow?> LatestAsync(CancellationToken cancellationToken) =>
        await OneAsync(Select + " ORDER BY r.created_at DESC, r.id DESC LIMIT 1", null, cancellationToken);

    public ValueTask<bool> MovePhaseAsync(Guid id, string from, string to, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("UPDATE research.sim_days_runs SET phase = @to, updated_at = @now WHERE id = @id AND phase = @from", id, now, c =>
        {
            c.Parameters.AddWithValue("from", NpgsqlDbType.Varchar, from);
            c.Parameters.AddWithValue("to", NpgsqlDbType.Varchar, to);
        }, cancellationToken);

    public ValueTask<bool> RequestSettleAsync(Guid id, bool manual, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("UPDATE research.sim_days_runs SET phase = 'settling', manual = @manual, waiting = NULL, updated_at = @now WHERE id = @id AND phase = 'idle'", id, now, c => c.Parameters.AddWithValue("manual", NpgsqlDbType.Boolean, manual), cancellationToken);

    public async ValueTask SetWaitingAsync(Guid id, string? reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        await UpdateAsync("UPDATE research.sim_days_runs SET waiting = @reason, updated_at = @now WHERE id = @id AND waiting IS DISTINCT FROM @reason AND phase IN ('idle', 'settling', 'settled')", id, now, c => c.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)Cap(reason, 40) ?? DBNull.Value), cancellationToken);

    public async ValueTask SetHoldOutNoteAsync(Guid id, string? note, DateTimeOffset now, CancellationToken cancellationToken) =>
        await UpdateAsync("UPDATE research.sim_days_runs SET hold_out_note = @note, updated_at = @now WHERE id = @id AND " + Active, id, now, c => c.Parameters.AddWithValue("note", NpgsqlDbType.Varchar, (object?)Cap(note, 1000) ?? DBNull.Value), cancellationToken);

    public ValueTask<bool> SetAutoAsync(Guid id, bool on, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("UPDATE research.sim_days_runs SET auto = @on, updated_at = @now WHERE id = @id AND " + Active, id, now, c => c.Parameters.AddWithValue("on", NpgsqlDbType.Boolean, on), cancellationToken);

    public ValueTask<bool> SettleAsync(Guid id, SimDayPending pending, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return UpdateAsync("""
            UPDATE research.sim_days_runs SET phase = 'settled', waiting = NULL, pending_session = @session, pending_record = @record, pending_trades = @trades, pending_equity = @equity, pending_hash = @hash, pending_end = @end_in_force, updated_at = @now
            WHERE id = @id AND phase = 'settling' AND cursor_session = @session
            """, id, now, c =>
        {
            c.Parameters.AddWithValue("session", NpgsqlDbType.Date, pending.Session);
            c.Parameters.AddWithValue("record", NpgsqlDbType.Jsonb, pending.RecordJson);
            c.Parameters.AddWithValue("trades", NpgsqlDbType.Jsonb, pending.TradesJson);
            c.Parameters.AddWithValue("equity", NpgsqlDbType.Numeric, pending.EquityClose);
            c.Parameters.AddWithValue("hash", NpgsqlDbType.Char, pending.ResultHash);
            c.Parameters.AddWithValue("end_in_force", NpgsqlDbType.Date, pending.EndInForce);
        }, cancellationToken);
    }

    public async ValueTask<bool> AdvanceAsync(Guid id, SimDaySnapshotRow snapshot, DateOnly? next, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var move = new NpgsqlCommand("""
            UPDATE research.sim_days_runs SET phase = CASE WHEN @next::date IS NULL THEN 'complete' ELSE 'idle' END, cursor_session = COALESCE(@next::date, cursor_session),
                pending_session = NULL, pending_record = NULL, pending_trades = NULL, pending_equity = NULL, pending_hash = NULL, pending_end = NULL, updated_at = @now
            WHERE id = @id AND phase = 'settled' AND cursor_session = @session
            """, connection, transaction))
        {
            move.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            move.Parameters.AddWithValue("next", NpgsqlDbType.Date, (object?)next ?? DBNull.Value);
            move.Parameters.AddWithValue("session", NpgsqlDbType.Date, snapshot.Session);
            move.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            if (await move.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO research.sim_days_snapshots (run_id, session, record, picks, blocked, trades, equity_close, result_hash, end_in_force, created_at)
            VALUES (@id, @session, @record, @picks, @blocked, @trades, @equity, @hash, @end_in_force, @now)
            """, connection, transaction))
        {
            var p = insert.Parameters;
            p.AddWithValue("id", NpgsqlDbType.Uuid, id);
            p.AddWithValue("session", NpgsqlDbType.Date, snapshot.Session);
            p.AddWithValue("record", NpgsqlDbType.Jsonb, snapshot.RecordJson);
            p.AddWithValue("picks", NpgsqlDbType.Jsonb, snapshot.PicksJson);
            p.AddWithValue("blocked", NpgsqlDbType.Jsonb, snapshot.BlockedJson);
            p.AddWithValue("trades", NpgsqlDbType.Jsonb, snapshot.TradesJson);
            p.AddWithValue("equity", NpgsqlDbType.Numeric, snapshot.EquityClose);
            p.AddWithValue("hash", NpgsqlDbType.Char, snapshot.ResultHash);
            p.AddWithValue("end_in_force", NpgsqlDbType.Date, snapshot.EndInForce);
            p.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<bool> ExtendAsync(Guid id, DateOnly end, DateOnly requestedEnd, DateOnly cursor, DateOnly? readThrough, string? holdOutNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await UpdateAsync("""
                UPDATE research.sim_days_runs SET phase = 'idle', range_end = @end, requested_end = GREATEST(requested_end, @requested), cursor_session = @cursor, read_through = @through, holdout_note = @note, updated_at = @now
                WHERE id = @id AND phase = 'complete'
                """, id, now, c =>
            {
                c.Parameters.AddWithValue("end", NpgsqlDbType.Date, end);
                c.Parameters.AddWithValue("requested", NpgsqlDbType.Date, requestedEnd);
                c.Parameters.AddWithValue("cursor", NpgsqlDbType.Date, cursor);
                c.Parameters.AddWithValue("through", NpgsqlDbType.Date, (object?)readThrough ?? DBNull.Value);
                c.Parameters.AddWithValue("note", NpgsqlDbType.Varchar, (object?)Cap(holdOutNote, 1000) ?? DBNull.Value);
            }, cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false; // another run is active
        }
    }

    public ValueTask<bool> FailAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("""
            UPDATE research.sim_days_runs SET phase = 'error', waiting = NULL, fail_reason = @reason, auto = false, pending_session = NULL, pending_record = NULL, pending_trades = NULL, pending_equity = NULL, pending_hash = NULL, pending_end = NULL, updated_at = @now
            WHERE id = @id AND
            """ + " " + Active, id, now, c => c.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, Cap(reason, 400)!), cancellationToken);

    public ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("""
            UPDATE research.sim_days_runs SET phase = 'cancelled', waiting = NULL, auto = false, pending_session = NULL, pending_record = NULL, pending_trades = NULL, pending_equity = NULL, pending_hash = NULL, pending_end = NULL, updated_at = @now
            WHERE id = @id AND phase IN ('idle', 'settling', 'settled', 'complete')
            """, id, now, null, cancellationToken);

    public async ValueTask<IReadOnlyList<SimDaySnapshotRow>> SnapshotsAsync(Guid id, CancellationToken cancellationToken) =>
        await SnapshotRowsAsync("WHERE run_id = @id ORDER BY session", id, null, cancellationToken);

    public async ValueTask<SimDaySnapshotRow?> SnapshotAsync(Guid id, DateOnly session, CancellationToken cancellationToken) =>
        (await SnapshotRowsAsync("WHERE run_id = @id AND session = @session", id, session, cancellationToken)).FirstOrDefault();

    public async ValueTask<SimTapeDay> TapeDayAsync(Guid id, DateOnly session, CancellationToken cancellationToken)
    {
        // a trade still open coming into the day: stored before it with no exit, and no earlier stored day has its exit (a trade is stored on its entry day and again on its exit day)
        await using var command = dataSource.CreateCommand("""
            SELECT
                (SELECT record::text FROM research.sim_days_snapshots WHERE run_id = @id AND session = @session),
                (SELECT trades::text FROM research.sim_days_snapshots WHERE run_id = @id AND session = @session),
                (SELECT equity_close FROM research.sim_days_snapshots WHERE run_id = @id AND session < @session ORDER BY session DESC LIMIT 1),
                (SELECT coalesce(jsonb_agg(t.value ORDER BY (t.value->>'tradeNo')::bigint), '[]'::jsonb)::text
                 FROM research.sim_days_snapshots s CROSS JOIN LATERAL jsonb_array_elements(s.trades) t
                 WHERE s.run_id = @id AND s.session < @session AND t.value->>'exitSession' IS NULL
                   -- shortcut: the NOT EXISTS unpacks every earlier snapshot per candidate trade (trades x days); measured 16-21 ms for 50 trades; rewrite as one GROUP BY over the earlier snapshots when runs exceed about a year
                   AND NOT EXISTS (
                       SELECT 1 FROM research.sim_days_snapshots s2 CROSS JOIN LATERAL jsonb_array_elements(s2.trades) t2
                       WHERE s2.run_id = @id AND s2.session < @session AND t2.value->>'tradeNo' = t.value->>'tradeNo' AND t2.value->>'exitSession' IS NOT NULL))
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("session", NpgsqlDbType.Date, session);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SimTapeDay(
            reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.GetString(3));
    }

    private async ValueTask<List<SimDaySnapshotRow>> SnapshotRowsAsync(string where, Guid id, DateOnly? session, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT session, record::text, picks::text, blocked::text, trades::text, equity_close, result_hash, end_in_force FROM research.sim_days_snapshots " + where);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        if (session is { } s)
        {
            command.Parameters.AddWithValue("session", NpgsqlDbType.Date, s);
        }

        var rows = new List<SimDaySnapshotRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SimDaySnapshotRow(
                reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetString(6), reader.GetFieldValue<DateOnly>(7)));
        }

        return rows;
    }

    private async ValueTask<bool> UpdateAsync(string sql, Guid id, DateTimeOffset now, Action<NpgsqlCommand>? bind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        bind?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async ValueTask<SimDaysRunRow?> OneAsync(string sql, Action<NpgsqlCommand>? bind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        bind?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        DateOnly? Date(int i) => reader.IsDBNull(i) ? null : reader.GetFieldValue<DateOnly>(i);
        string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        var pending = reader.IsDBNull(17) ? null : new SimDayPending(reader.GetFieldValue<DateOnly>(17), reader.GetString(18), reader.GetString(19), reader.GetDecimal(20), reader.GetString(21), reader.GetFieldValue<DateOnly>(22));
        return new SimDaysRunRow(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), Text(5), reader.GetDecimal(6), reader.GetString(7), reader.GetFieldValue<DateOnly>(8), reader.GetFieldValue<DateOnly>(9), reader.GetFieldValue<DateOnly>(10),
            reader.GetFieldValue<DateOnly>(11), reader.GetString(12), reader.GetBoolean(13), Date(14), Text(15), Text(16), pending, reader.GetInt32(23), reader.IsDBNull(24) ? null : reader.GetDecimal(24),
            reader.GetFieldValue<DateTimeOffset>(25), reader.GetFieldValue<DateTimeOffset>(26), reader.GetBoolean(27), Text(28));
    }

    private static string? Cap(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}
