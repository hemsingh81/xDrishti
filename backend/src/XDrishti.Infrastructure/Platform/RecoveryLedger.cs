using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Infrastructure.Platform;

/// <summary><c>platform.recovery_passes</c> and <c>platform.recovery_items</c> (ADR 0240): plain SQL, one statement each; an item is unique per service and occurrence, so a repeated pass updates the row instead of adding one. No delete.</summary>
internal sealed class RecoveryLedger(NpgsqlDataSource dataSource) : IRecoveryLedger
{
    public async ValueTask<Guid> BeginPassAsync(string trigger, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        await using var command = dataSource.CreateCommand("INSERT INTO platform.recovery_passes (id, trigger, started_at, status) VALUES (@id, @trigger, @at, 'running')");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        command.Parameters.AddWithValue("trigger", NpgsqlDbType.Varchar, trigger);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    public async ValueTask EndPassAsync(Guid passId, string status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE platform.recovery_passes SET status = @status, finished_at = @at WHERE id = @id AND status = 'running'");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, passId);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Varchar, status);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> InterruptRunningAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand("UPDATE platform.recovery_items SET state = 'failed', reason = 'interrupted' WHERE state = 'running'"));
        var passes = new NpgsqlBatchCommand("UPDATE platform.recovery_passes SET status = 'interrupted', finished_at = @at WHERE status = 'running'");
        passes.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now);
        batch.BatchCommands.Add(passes);
        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        return reader.RecordsAffected;
    }

    public async ValueTask<RecoveryItemRow?> FindAsync(string serviceId, string occurrence, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM platform.recovery_items WHERE service_id = @service AND occurrence = @occurrence");
        command.Parameters.AddWithValue("service", NpgsqlDbType.Varchar, serviceId);
        command.Parameters.AddWithValue("occurrence", NpgsqlDbType.Varchar, occurrence);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Read(r) : null;
    }

    public async ValueTask<RecoveryItemRow?> FindNewestAsync(string serviceId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM platform.recovery_items WHERE service_id = @service ORDER BY occurrence DESC LIMIT 1");
        command.Parameters.AddWithValue("service", NpgsqlDbType.Varchar, serviceId);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Read(r) : null;
    }

    public async ValueTask<IReadOnlyList<RecoveryItemRow>> ListRetryableAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM platform.recovery_items WHERE state = 'failed' AND attempts < @max ORDER BY occurrence LIMIT 500");
        command.Parameters.AddWithValue("max", NpgsqlDbType.Integer, maxAttempts);
        var rows = new List<RecoveryItemRow>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(Read(r));
        }

        return rows;
    }

    private const string Columns = "id, pass_id, service_id, occurrence, session_from, session_to, state, reason, attempts, run_id";

    private static RecoveryItemRow Read(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateOnly>(4), r.GetFieldValue<DateOnly>(5), r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt16(8), r.IsDBNull(9) ? null : r.GetGuid(9));

    public async ValueTask SaveAsync(RecoveryItemRow item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO platform.recovery_items (id, pass_id, service_id, occurrence, session_from, session_to, state, reason, attempts, run_id)
            VALUES (@id, @pass, @service, @occurrence, @from, @to, @state, @reason, @attempts, @run)
            ON CONFLICT (service_id, occurrence) DO UPDATE SET pass_id = EXCLUDED.pass_id, session_from = EXCLUDED.session_from, session_to = EXCLUDED.session_to,
                state = EXCLUDED.state, reason = EXCLUDED.reason, attempts = EXCLUDED.attempts, run_id = EXCLUDED.run_id
            """);
        var p = command.Parameters;
        p.AddWithValue("id", NpgsqlDbType.Uuid, item.Id);
        p.AddWithValue("pass", NpgsqlDbType.Uuid, item.PassId);
        p.AddWithValue("service", NpgsqlDbType.Varchar, item.ServiceId);
        p.AddWithValue("occurrence", NpgsqlDbType.Varchar, item.Occurrence);
        p.AddWithValue("from", NpgsqlDbType.Date, item.SessionFrom);
        p.AddWithValue("to", NpgsqlDbType.Date, item.SessionTo);
        p.AddWithValue("state", NpgsqlDbType.Varchar, item.State);
        p.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)item.Reason ?? DBNull.Value });
        p.AddWithValue("attempts", NpgsqlDbType.Smallint, (short)item.Attempts);
        p.Add(new NpgsqlParameter("run", NpgsqlDbType.Uuid) { Value = item.RunId is { } run ? run : DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<RecoveryExhaustedRow>> ListExhaustedAsync(int maxAttempts, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT i.service_id, i.occurrence, i.reason, p.started_at FROM platform.recovery_items i JOIN platform.recovery_passes p ON p.id = i.pass_id
            WHERE i.state = 'failed' AND i.attempts >= @max AND p.started_at >= @since ORDER BY p.started_at DESC LIMIT 200
            """);
        command.Parameters.AddWithValue("max", NpgsqlDbType.Integer, maxAttempts);
        command.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since);
        var rows = new List<RecoveryExhaustedRow>();
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            rows.Add(new RecoveryExhaustedRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }
}
