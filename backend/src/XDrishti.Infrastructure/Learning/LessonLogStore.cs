using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.lessons</c> behind <see cref="ILessonLog"/> (spec P6-16, ADR 0078): raw SQL, insert and select only (the runtime role has no more).</summary>
internal sealed class LessonLogStore(NpgsqlDataSource dataSource) : ILessonLog
{
    private const string Columns = "id, lesson_id, state, from_state, monitor_kind, subject, evidence_key, problem, hypothesis_ref, experiment_id, result, experiment_outcome, before_mean_r, after_mean_r, before_total_r, after_total_r, reason, actor, created_at";

    // The newest row of each lesson.
    private const string Latest = $"""
        SELECT DISTINCT ON (COALESCE(lesson_id, id)) {Columns} FROM research.lessons ORDER BY COALESCE(lesson_id, id), id DESC
        """;

    private const string GrowingTag = "growing_tag";

    public async ValueTask<LessonAppend> OpenAsync(NewLessonRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // One opener per finding at a time, so the check and the insert cannot interleave (a growing tag has no unique index: its key is reused after a lesson is final).
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, $"research.lessons.open:{row.MonitorKind}|{row.Subject}|{row.EvidenceKey}");
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await FindExistingAsync(connection, transaction, row.MonitorKind, row.Subject, row.EvidenceKey, cancellationToken) is { } known)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new LessonAppend(known, LessonAppendStatus.Existing);
        }

        var id = await InsertAsync(connection, transaction, row, onConflictDoNothing: true, cancellationToken);
        if (id is { } created)
        {
            await transaction.CommitAsync(cancellationToken);
            return new LessonAppend(created, LessonAppendStatus.Created);
        }

        await transaction.RollbackAsync(cancellationToken);
        return new LessonAppend(0, LessonAppendStatus.Stale);
    }

    public async ValueTask<LessonAppend> AppendAsync(NewLessonRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lessonId = row.LessonId ?? throw new ArgumentException("A state change names its lesson.", nameof(row));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, "research.lessons:" + lessonId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var latest = new NpgsqlCommand("SELECT state FROM research.lessons WHERE (id = @id AND lesson_id IS NULL) OR lesson_id = @id ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            latest.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, lessonId);
            if (await latest.ExecuteScalarAsync(cancellationToken) is not string state || state != row.FromState)
            {
                return new LessonAppend(0, LessonAppendStatus.Stale);
            }
        }

        var id = await InsertAsync(connection, transaction, row, onConflictDoNothing: false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LessonAppend(id!.Value, LessonAppendStatus.Created);
    }

    public async ValueTask<long?> FindIdByEvidenceAsync(string monitorKind, string subject, string evidenceKey, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await FindExistingAsync(connection, null, monitorKind, subject, evidenceKey, cancellationToken);
    }

    // A growing tag: the key is the tag kind (rows written before ADR 0078's amendment carry the kind and counts, "Kind:a/b->c/d", and still count); a lesson blocks while its newest row is open or linked.
    // Every other monitor: the exact key, in any state.
    private static async Task<long?> FindExistingAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string monitorKind, string subject, string evidenceKey, CancellationToken cancellationToken)
    {
        var growing = monitorKind == GrowingTag;
        var sql = growing
            ? """
              SELECT o.id FROM research.lessons o
              WHERE o.lesson_id IS NULL AND o.monitor_kind = @m AND o.subject = @s AND (o.evidence_key = @e OR starts_with(o.evidence_key, @e || ':'))
                AND (SELECT x.state FROM research.lessons x WHERE x.id = o.id OR x.lesson_id = o.id ORDER BY x.id DESC LIMIT 1) IN ('open', 'linked')
              ORDER BY o.id LIMIT 1
              """
            : "SELECT id FROM research.lessons WHERE lesson_id IS NULL AND monitor_kind = @m AND subject = @s AND evidence_key = @e";
        await using var select = new NpgsqlCommand(sql, connection, transaction);
        select.Parameters.AddWithValue("m", NpgsqlDbType.Varchar, monitorKind);
        select.Parameters.AddWithValue("s", NpgsqlDbType.Varchar, subject);
        select.Parameters.AddWithValue("e", NpgsqlDbType.Varchar, evidenceKey);
        return await select.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    public async ValueTask<LessonRow?> FindAsync(long lessonId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.lessons WHERE (id = @id AND lesson_id IS NULL) OR lesson_id = @id ORDER BY id DESC LIMIT 1");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, lessonId);
        var rows = await ReadAllAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<LessonRow>> HistoryAsync(long lessonId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.lessons WHERE (id = @id AND lesson_id IS NULL) OR lesson_id = @id ORDER BY id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, lessonId);
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<LessonListed>> ListAsync(string? state, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT l.*, (SELECT o.created_at FROM research.lessons o WHERE o.id = COALESCE(l.lesson_id, l.id)) AS opened_at FROM ({Latest}) l WHERE (@state::text IS NULL OR l.state = @state::text) ORDER BY COALESCE(l.lesson_id, l.id) DESC LIMIT @limit");
        select.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var listed = new List<LessonListed>();
        while (await reader.ReadAsync(cancellationToken))
        {
            listed.Add(new LessonListed(Read(reader), reader.GetFieldValue<DateTimeOffset>(19)));
        }

        return listed;
    }

    private static async Task<long?> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewLessonRow row, bool onConflictDoNothing, CancellationToken cancellationToken)
    {
        var conflict = onConflictDoNothing ? " ON CONFLICT (monitor_kind, subject, evidence_key) WHERE lesson_id IS NULL AND monitor_kind <> 'growing_tag' DO NOTHING" : string.Empty;
        await using var insert = new NpgsqlCommand($"""
            INSERT INTO research.lessons (lesson_id, state, from_state, monitor_kind, subject, evidence_key, problem, hypothesis_ref, experiment_id, result, experiment_outcome, before_mean_r, after_mean_r, before_total_r, after_total_r, reason, actor, created_at)
            VALUES (@lid, @state, @from, @monitor, @subject, @evidence, @problem, @href, @exp, @result, @outcome, @bmean, @amean, @btotal, @atotal, @reason, @actor, @at){conflict}
            RETURNING id
            """, connection, transaction);
        var p = insert.Parameters;
        p.Add(new NpgsqlParameter("lid", NpgsqlDbType.Bigint) { Value = row.LessonId is { } l ? l : DBNull.Value });
        p.AddWithValue("state", NpgsqlDbType.Varchar, row.State);
        p.Add(new NpgsqlParameter("from", NpgsqlDbType.Varchar) { Value = (object?)row.FromState ?? DBNull.Value });
        p.AddWithValue("monitor", NpgsqlDbType.Varchar, row.MonitorKind);
        p.AddWithValue("subject", NpgsqlDbType.Varchar, row.Subject);
        p.AddWithValue("evidence", NpgsqlDbType.Varchar, row.EvidenceKey);
        p.AddWithValue("problem", NpgsqlDbType.Varchar, row.Problem);
        p.Add(new NpgsqlParameter("href", NpgsqlDbType.Bigint) { Value = row.HypothesisRef is { } h ? h : DBNull.Value });
        p.Add(new NpgsqlParameter("exp", NpgsqlDbType.Bigint) { Value = row.ExperimentId is { } e ? e : DBNull.Value });
        p.Add(new NpgsqlParameter("result", NpgsqlDbType.Varchar) { Value = (object?)row.Result ?? DBNull.Value });
        p.Add(new NpgsqlParameter("outcome", NpgsqlDbType.Varchar) { Value = (object?)row.ExperimentOutcome ?? DBNull.Value });
        p.Add(new NpgsqlParameter("bmean", NpgsqlDbType.Double) { Value = row.BeforeMeanR is { } a ? a : DBNull.Value });
        p.Add(new NpgsqlParameter("amean", NpgsqlDbType.Double) { Value = row.AfterMeanR is { } b ? b : DBNull.Value });
        p.Add(new NpgsqlParameter("btotal", NpgsqlDbType.Double) { Value = row.BeforeTotalR is { } c ? c : DBNull.Value });
        p.Add(new NpgsqlParameter("atotal", NpgsqlDbType.Double) { Value = row.AfterTotalR is { } d ? d : DBNull.Value });
        p.AddWithValue("reason", NpgsqlDbType.Varchar, row.Reason);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, row.Actor);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, row.CreatedAt);
        return await insert.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
    }

    private static async Task<IReadOnlyList<LessonRow>> ReadAllAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<LessonRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static string? Text(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long? Whole(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static double? Real(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static LessonRow Read(DbDataReader r) => new(
        r.GetInt64(0),
        new NewLessonRow(Whole(r, 1), r.GetString(2), Text(r, 3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), Whole(r, 8), Whole(r, 9), Text(r, 10), Text(r, 11), Real(r, 12), Real(r, 13), Real(r, 14), Real(r, 15), r.GetString(16), r.GetString(17), r.GetFieldValue<DateTimeOffset>(18)));
}
