using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The configuration record behind <see cref="IGovernanceStore"/> (spec P10-05, ADR 0119): raw SQL, select and insert only (the runtime role has no more). A snapshot is unique per session day and hash, so a run repeated on the same day stores it once.
/// Nothing here changes a setting or talks to a broker.
/// </summary>
internal sealed class GovernanceStore(NpgsqlDataSource dataSource) : IGovernanceStore
{
    private const string EpochColumns = "e.id, e.started_on, e.started_at, e.kind, e.reason, e.actor";

    private const string Actor = "system:paper-capture";

    public async ValueTask<StoredSnapshot?> LatestSnapshotAsync(bool fullOnly, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, taken_at, session_date, hash, components::text, partial FROM paper.config_snapshots WHERE (@full = false OR partial = false) ORDER BY id DESC LIMIT 1");
        select.Parameters.AddWithValue("full", NpgsqlDbType.Boolean, fullOnly);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredSnapshot(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5))
            : null;
    }

    public async ValueTask<GovernanceCommitted> CommitAsync(GovernanceCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var s = commit.Snapshot;
        long snapshotId;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO paper.config_snapshots (taken_at, session_date, hash, components, partial, process)
            VALUES (@at, @session, @hash, @components::jsonb, @partial, @process)
            ON CONFLICT (session_date, hash) DO NOTHING
            RETURNING id
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, s.TakenAt);
            insert.Parameters.AddWithValue("session", NpgsqlDbType.Date, s.SessionDate);
            insert.Parameters.AddWithValue("hash", NpgsqlDbType.Char, s.Hash);
            insert.Parameters.AddWithValue("components", NpgsqlDbType.Text, s.ComponentsJson);
            insert.Parameters.AddWithValue("partial", NpgsqlDbType.Boolean, s.Partial);
            insert.Parameters.AddWithValue("process", NpgsqlDbType.Varchar, s.Process);
            var id = await insert.ExecuteScalarAsync(cancellationToken);
            if (id is long created)
            {
                snapshotId = created;
            }
            else
            {
                await using var existing = new NpgsqlCommand("SELECT id FROM paper.config_snapshots WHERE session_date = @session AND hash = @hash", connection, transaction);
                existing.Parameters.AddWithValue("session", NpgsqlDbType.Date, s.SessionDate);
                existing.Parameters.AddWithValue("hash", NpgsqlDbType.Char, s.Hash);
                snapshotId = (long)(await existing.ExecuteScalarAsync(cancellationToken))!;
            }
        }

        var epochId = commit.CurrentEpochId;
        if (commit.NewEpoch is { } e)
        {
            await using var epoch = new NpgsqlCommand("""
                INSERT INTO paper.epochs (started_on, started_at, kind, fingerprint_hash, reason, actor, prev_epoch_id)
                VALUES (@on, @at, 'detected_change', @hash, @reason, @actor, @prev)
                RETURNING id
                """, connection, transaction);
            epoch.Parameters.AddWithValue("on", NpgsqlDbType.Date, e.StartedOn);
            epoch.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, e.At);
            epoch.Parameters.AddWithValue("hash", NpgsqlDbType.Char, e.Hash);
            epoch.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, e.Reason);
            epoch.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, Actor);
            epoch.Parameters.AddWithValue("prev", NpgsqlDbType.Bigint, commit.CurrentEpochId);
            epochId = (long)(await epoch.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var c in commit.Changes)
        {
            await using var change = new NpgsqlCommand("""
                INSERT INTO paper.config_changes (from_snapshot_id, to_snapshot_id, "key", "class", "before", "after", effect, epoch_id, explained_by)
                VALUES (@from, @to, @key, @class, @before, @after, @effect, @epoch, @explained)
                """, connection, transaction);
            change.Parameters.AddWithValue("from", NpgsqlDbType.Bigint, commit.FromSnapshotId ?? snapshotId);
            change.Parameters.AddWithValue("to", NpgsqlDbType.Bigint, snapshotId);
            change.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, c.Key);
            change.Parameters.AddWithValue("class", NpgsqlDbType.Varchar, c.Class);
            change.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Text) { Value = (object?)c.Before ?? DBNull.Value });
            change.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Text) { Value = (object?)c.After ?? DBNull.Value });
            change.Parameters.AddWithValue("effect", NpgsqlDbType.Varchar, c.Effect);
            change.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
            change.Parameters.Add(new NpgsqlParameter("explained", NpgsqlDbType.Varchar) { Value = (object?)c.ExplainedBy ?? DBNull.Value });
            await change.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new GovernanceCommitted(snapshotId, epochId);
    }

    public async ValueTask<EpochRow> CreateFirstEpochAsync(DateOnly startedOn, DateTimeOffset at, string? hash, CancellationToken cancellationToken)
    {
        await using (var insert = dataSource.CreateCommand("""
            INSERT INTO paper.epochs (started_on, started_at, kind, fingerprint_hash, reason, actor)
            SELECT @on, @at, 'first', @hash, 'the default epoch, started with the first capture', 'system:paper-capture'
            WHERE NOT EXISTS (SELECT 1 FROM paper.epochs)
            ON CONFLICT DO NOTHING
            """))
        {
            insert.Parameters.AddWithValue("on", NpgsqlDbType.Date, startedOn);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
            insert.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Char) { Value = (object?)hash ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = dataSource.CreateCommand($"SELECT {EpochColumns} FROM paper.epochs e ORDER BY e.id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Epoch(reader);
    }

    public async ValueTask<EpochRow?> StartOwnerEpochAsync(NewOwnerEpoch epoch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        await using var insert = dataSource.CreateCommand($"""
            WITH ins AS (
                INSERT INTO paper.epochs (started_on, started_at, kind, fingerprint_hash, reason, actor, prev_epoch_id)
                SELECT @on, @at, 'owner_restart', @hash, @reason, @actor, @prev
                WHERE (SELECT max(id) FROM paper.epochs) = @prev
                RETURNING id, started_on, started_at, kind, reason, actor
            )
            SELECT id, started_on, started_at, kind, reason, actor FROM ins
            """);
        insert.Parameters.AddWithValue("on", NpgsqlDbType.Date, epoch.StartedOn);
        insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, epoch.At);
        insert.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Char) { Value = (object?)epoch.Hash ?? DBNull.Value });
        insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, epoch.Reason);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, epoch.Actor);
        insert.Parameters.AddWithValue("prev", NpgsqlDbType.Bigint, epoch.PrevEpochId);
        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Epoch(reader) : null;
    }

    public async ValueTask<IReadOnlyList<EpochSummary>> ListEpochsAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {EpochColumns}, e.fingerprint_hash,
                   (SELECT count(*) FROM paper.entries n WHERE n.epoch_id = e.id AND n.measure_status = 'measured')::int,
                   (SELECT count(*) FROM paper.entries n WHERE n.epoch_id = e.id AND n.measure_status = 'straddle')::int,
                   (SELECT count(*) FROM paper.config_changes c WHERE c.epoch_id = e.id AND c."class" = 'risk_down')::int,
                   (SELECT count(*) FROM paper.config_changes c WHERE c.epoch_id = e.id AND c."class" <> 'free' AND c.explained_by IS NULL
                        AND NOT EXISTS (SELECT 1 FROM paper.epoch_notes n WHERE n.change_id = c.id))::int
            FROM paper.epochs e ORDER BY e.id DESC
            """);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var result = new List<EpochSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new EpochSummary(Epoch(reader), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)));
        }

        return result;
    }

    public async ValueTask<IReadOnlyList<StoredChange>> ListChangesAsync(long? epochId, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"{ChangeSelect} WHERE (@epoch::bigint IS NULL OR epoch_id = @epoch) ORDER BY id DESC LIMIT @limit");
        select.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = (object?)epochId ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadChangesAsync(select, cancellationToken);
    }

    public async ValueTask<StoredChange?> FindChangeAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"{ChangeSelect} WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        return (await ReadChangesAsync(select, cancellationToken)).FirstOrDefault();
    }

    public async ValueTask<IReadOnlyList<EpochNote>> ListNotesAsync(long? epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, epoch_id, change_id, hypothesis_id, text, actor, created_at FROM paper.epoch_notes WHERE (@epoch::bigint IS NULL OR epoch_id = @epoch) ORDER BY id DESC LIMIT 200");
        select.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = (object?)epochId ?? DBNull.Value });
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var result = new List<EpochNote>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new EpochNote(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return result;
    }

    public async ValueTask AddNoteAsync(long epochId, long? changeId, long? hypothesisId, string text, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var insert = dataSource.CreateCommand("INSERT INTO paper.epoch_notes (epoch_id, change_id, hypothesis_id, text, actor, created_at) VALUES (@epoch, @change, @hypothesis, @text, @actor, @at)");
        insert.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        insert.Parameters.Add(new NpgsqlParameter("change", NpgsqlDbType.Bigint) { Value = changeId is { } c ? c : DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("hypothesis", NpgsqlDbType.Bigint) { Value = hypothesisId is { } h ? h : DBNull.Value });
        insert.Parameters.AddWithValue("text", NpgsqlDbType.Varchar, text);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlySet<long>> UsedHypothesisIdsAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT substring(explained_by from 12)::bigint FROM paper.config_changes WHERE explained_by ~ '^hypothesis:[0-9]+$'
            UNION
            SELECT hypothesis_id FROM paper.epoch_notes WHERE hypothesis_id IS NOT NULL
            """);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var result = new HashSet<long>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    private const string ChangeSelect = "SELECT id, from_snapshot_id, to_snapshot_id, \"key\", \"class\", \"before\", \"after\", effect, epoch_id, explained_by, (SELECT s.session_date FROM paper.config_snapshots s WHERE s.id = to_snapshot_id) FROM paper.config_changes";

    private static async Task<List<StoredChange>> ReadChangesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredChange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StoredChange(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7), reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10)));
        }

        return result;
    }

    private static EpochRow Epoch(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
}
