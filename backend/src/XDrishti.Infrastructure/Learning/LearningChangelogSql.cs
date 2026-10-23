using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>The one INSERT into <c>research.learning_changelog</c> (spec P6-08), shared by the changelog and by the model registry, which writes its champion designations in the same transaction.</summary>
internal static class LearningChangelogSql
{
    public static async Task<long> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewChangelogEntry e, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO research.learning_changelog (kind, subject_kind, subject, from_state, to_state, reason, actor, evidence_ids, before_ref, after_ref, paper_days, reverts_id, created_at)
            VALUES (@kind, @subject_kind, @subject, @from, @to, @reason, @actor, @evidence, @before, @after, @paper, @reverts, @at)
            RETURNING id
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("kind", NpgsqlDbType.Varchar, e.Kind);
        p.AddWithValue("subject_kind", NpgsqlDbType.Varchar, e.SubjectKind);
        p.AddWithValue("subject", NpgsqlDbType.Varchar, e.Subject);
        p.AddWithValue("from", NpgsqlDbType.Varchar, e.FromState);
        p.AddWithValue("to", NpgsqlDbType.Varchar, e.ToState);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, e.Reason);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, e.Actor);
        p.AddWithValue("evidence", NpgsqlDbType.Array | NpgsqlDbType.Text, e.EvidenceIds.ToArray());
        p.Add(new NpgsqlParameter("before", NpgsqlDbType.Varchar) { Value = (object?)e.BeforeRef ?? DBNull.Value });
        p.Add(new NpgsqlParameter("after", NpgsqlDbType.Varchar) { Value = (object?)e.AfterRef ?? DBNull.Value });
        p.Add(new NpgsqlParameter("paper", NpgsqlDbType.Integer) { Value = e.PaperDays is { } d ? d : DBNull.Value });
        p.Add(new NpgsqlParameter("reverts", NpgsqlDbType.Bigint) { Value = e.RevertsId is { } r ? r : DBNull.Value });
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, e.At);
        return (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
    }
}
