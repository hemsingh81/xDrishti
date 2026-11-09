using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LearnedSettings;

namespace XDrishti.Infrastructure.LearnedSettings;

/// <summary>
/// <c>config.learned_setting_changes</c> behind <see cref="ILearnedSettingStore"/> and the start-up read <see cref="ILearnedOverrideSource"/> (spec AU-10, ADR 0242, ADR 0247). Select and insert, and the conditional moves of the state column; the database refuses everything else
/// (column grants, a transition trigger, constraints that stop anything but a tightening by learning from ever being decided by <c>auto</c>, and a trigger that stops an automatic decision while <c>auto.enabled</c> is not stored as true). The time of a row is the database clock.
/// </summary>
internal sealed class LearnedSettingChanges(NpgsqlDataSource dataSource) : ILearnedSettingStore, ILearnedOverrideSource
{
    private const string Columns = "id, setting_key, old_value, new_value, direction, source, state, decided_by, decided_at, undone_by, undone_at, snapshot_hash, snapshot::text, evidence_ref, evidence_hash, undo_of, created_at";

    private const string AppliedSql = "SELECT DISTINCT ON (setting_key) setting_key, new_value, decided_by FROM config.learned_setting_changes WHERE state = 'applied' ORDER BY setting_key, id DESC";

    public async ValueTask<IReadOnlyList<LearnedChangeRow>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM config.learned_setting_changes ORDER BY id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 500));
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<LearnedChangeRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM config.learned_setting_changes WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        var rows = await ReadAsync(command, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> AppliedValuesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(AppliedSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    public async ValueTask<int> AutoAppliedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM config.learned_setting_changes WHERE decided_by = 'auto' AND decided_at >= @since");
        command.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, since);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask<long?> InsertProposedAsync(NewLearnedChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var command = dataSource.CreateCommand(InsertSql);
        Bind(command, change);
        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    public async ValueTask<bool> ApplyAsync(long id, string decidedBy, string? expectedApplied, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var key = await LockKeyAsync(connection, transaction, id, cancellationToken);
            if (key is null)
            {
                return false;
            }

            await using var newest = new NpgsqlCommand("SELECT new_value FROM config.learned_setting_changes WHERE setting_key = @key AND state = 'applied' ORDER BY id DESC LIMIT 1", connection, transaction);
            newest.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, key);
            if (await newest.ExecuteScalarAsync(cancellationToken) as string != expectedApplied)
            {
                return false; // another decision for this key won while the caller was checking
            }

            await using var update = new NpgsqlCommand("UPDATE config.learned_setting_changes SET state = 'applied', decided_by = @by, decided_at = now() WHERE id = @id AND state = 'proposed'", connection, transaction);
            update.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, decidedBy);
            update.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return false; // the guard trigger or a check of the table refuses this decision; nothing was changed
        }
    }

    public async ValueTask<bool> DismissAsync(long id, string decidedBy, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("UPDATE config.learned_setting_changes SET state = 'dismissed', decided_by = @by, decided_at = now() WHERE id = @id AND state = 'proposed'");
        command.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, decidedBy);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return false;
        }
    }

    public async ValueTask<long?> UndoAsync(long id, NewLearnedChange undo, string owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(undo);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var key = await LockKeyAsync(connection, transaction, id, cancellationToken);
            if (key is null)
            {
                return null;
            }

            await using var newest = new NpgsqlCommand("SELECT id FROM config.learned_setting_changes WHERE setting_key = @key AND state = 'applied' ORDER BY id DESC LIMIT 1", connection, transaction);
            newest.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, key);
            if (await newest.ExecuteScalarAsync(cancellationToken) as long? != id)
            {
                return null; // only the newest applied row of a key is undone
            }

            await using var mark = new NpgsqlCommand("UPDATE config.learned_setting_changes SET state = 'undone', undone_by = @by WHERE id = @id AND state = 'applied'", connection, transaction);
            mark.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, owner);
            mark.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
            if (await mark.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return null;
            }

            await using var insert = new NpgsqlCommand(InsertSql, connection, transaction);
            Bind(insert, undo);
            if (await insert.ExecuteScalarAsync(cancellationToken) is not long newId)
            {
                return null;
            }

            await using var apply = new NpgsqlCommand("UPDATE config.learned_setting_changes SET state = 'applied', decided_by = @by, decided_at = now() WHERE id = @id AND state = 'proposed'", connection, transaction);
            apply.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, owner);
            apply.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, newId);
            if (await apply.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return null;
            }

            await transaction.CommitAsync(cancellationToken);
            return newId;
        }
        catch (PostgresException refusal) when (refusal.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation)
        {
            return null; // refused by the database (or undone already); the transaction rolls back
        }
    }

    /// <summary>Takes a transaction lock on the key of row <paramref name="id"/> and returns the key (null when there is no such row), so that decisions for one key are checked and written one at a time.</summary>
    private static async ValueTask<string?> LockKeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long id, CancellationToken cancellationToken)
    {
        await using var read = new NpgsqlCommand("SELECT setting_key FROM config.learned_setting_changes WHERE id = @id", connection, transaction);
        read.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        if (await read.ExecuteScalarAsync(cancellationToken) as string is not { } key)
        {
            return null;
        }

        await using var @lock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('config.learned:' || @key, 0))", connection, transaction);
        @lock.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, key);
        await @lock.ExecuteNonQueryAsync(cancellationToken);
        return key;
    }

    public IReadOnlyList<LearnedOverride>? ReadApplied()
    {
        try
        {
            using var command = dataSource.CreateCommand(AppliedSql);
            using var reader = command.ExecuteReader();
            var list = new List<LearnedOverride>();
            while (reader.Read())
            {
                list.Add(new LearnedOverride(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            return list;
        }
        catch (Exception failure) when (failure is DbException or InvalidOperationException or TimeoutException)
        {
            return null; // not readable (no table yet, no database): the host starts with the configured values
        }
    }

    private const string InsertSql = """
        INSERT INTO config.learned_setting_changes (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash, undo_of)
        VALUES (@key, @old, @new, @direction, @source, @snapshot, @hash, @ref, @evidence, @undo)
        ON CONFLICT (setting_key, evidence_hash) DO NOTHING
        RETURNING id
        """;

    private static void Bind(NpgsqlCommand command, NewLearnedChange c)
    {
        command.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, c.Key);
        command.Parameters.AddWithValue("old", NpgsqlDbType.Varchar, c.OldValue);
        command.Parameters.AddWithValue("new", NpgsqlDbType.Varchar, c.NewValue);
        command.Parameters.AddWithValue("direction", NpgsqlDbType.Varchar, c.Direction);
        command.Parameters.AddWithValue("source", NpgsqlDbType.Varchar, c.Source);
        command.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, c.Snapshot);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, c.SnapshotHash);
        command.Parameters.AddWithValue("ref", NpgsqlDbType.Varchar, c.EvidenceRef);
        command.Parameters.AddWithValue("evidence", NpgsqlDbType.Varchar, c.EvidenceHash);
        command.Parameters.Add(new NpgsqlParameter("undo", NpgsqlDbType.Bigint) { Value = (object?)c.UndoOf ?? DBNull.Value });
    }

    private static async ValueTask<IReadOnlyList<LearnedChangeRow>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<LearnedChangeRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LearnedChangeRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetInt64(15), reader.GetFieldValue<DateTimeOffset>(16)));
        }

        return rows;
    }
}

/// <summary>Whether the owner stored <c>auto.enabled</c> as true in <c>platform.app_settings</c>. No row, any other text, or a row that cannot be read is OFF.</summary>
internal sealed class AutoSwitch(NpgsqlDataSource dataSource) : IAutoSwitch
{
    public const string Key = "auto.enabled";

    public async ValueTask<bool> IsOnAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT value FROM platform.app_settings WHERE key = @key");
        command.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, Key);
        return await command.ExecuteScalarAsync(cancellationToken) is string value && string.Equals(value, "true", StringComparison.Ordinal);
    }
}
