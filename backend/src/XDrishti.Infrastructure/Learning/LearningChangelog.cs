using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.learning_changelog</c> behind <see cref="ILearningChangelog"/> (spec P6-08, ADR 0073): raw SQL, insert and select only (the runtime role has no more).</summary>
internal sealed class LearningChangelog(NpgsqlDataSource dataSource) : ILearningChangelog
{
    private const string Columns = "id, kind, subject_kind, subject, from_state, to_state, reason, actor, evidence_ids, before_ref, after_ref, paper_days, reverts_id, created_at";

    public async ValueTask<ChangelogAppend> AppendAsync(NewChangelogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, "research.learning_changelog:" + entry.Subject);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        // Checked again under the lock: the caller replayed the rows before this one was written.
        long? latestId = null;
        var state = entry.SubjectKind == "cell" ? "research" : string.Empty;
        await using (var latest = new NpgsqlCommand("SELECT id, to_state FROM research.learning_changelog WHERE subject = @s ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            latest.Parameters.AddWithValue("s", NpgsqlDbType.Varchar, entry.Subject);
            await using var reader = await latest.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                latestId = reader.GetInt64(0);
                state = reader.GetString(1);
            }
        }

        if (state != entry.FromState || (entry.RevertsId is { } target && latestId != target))
        {
            return new ChangelogAppend(0, true);
        }

        var id = await LearningChangelogSql.InsertAsync(connection, transaction, entry, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ChangelogAppend(id, false);
    }

    public async ValueTask<ChangelogEntry?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.learning_changelog WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<ChangelogEntry>> ListAsync(string? subjectKind, string? subject, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT {Columns} FROM research.learning_changelog
            WHERE (@kind::text IS NULL OR subject_kind = @kind::text) AND (@subject::text IS NULL OR subject = @subject::text)
            ORDER BY id DESC LIMIT @limit
            """);
        select.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = (object?)subjectKind ?? DBNull.Value });
        select.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Text) { Value = (object?)subject ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ChangelogEntry>> HistoryAsync(string subject, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.learning_changelog WHERE subject = @s ORDER BY id");
        select.Parameters.AddWithValue("s", NpgsqlDbType.Varchar, subject);
        return await ReadAllAsync(select, cancellationToken);
    }

    private static async Task<IReadOnlyList<ChangelogEntry>> ReadAllAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ChangelogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static ChangelogEntry Read(DbDataReader r) => new(
        r.GetInt64(0),
        new NewChangelogEntry(
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetString(6),
            r.GetString(7),
            r.GetFieldValue<string[]>(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetInt32(11),
            r.IsDBNull(12) ? null : r.GetInt64(12),
            r.GetFieldValue<DateTimeOffset>(13)));
}
