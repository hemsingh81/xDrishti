using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The suggestion inbox behind <see cref="ISuggestionStore"/> (spec P7-09, ADR 0092): raw SQL, insert and select only (the runtime role has no more). The same plan, kind and subject is stored once (<c>ON CONFLICT DO NOTHING</c> on the unique index),
/// so generating twice stores once. Nothing here talks to a broker.
/// </summary>
internal sealed class SuggestionStore(NpgsqlDataSource dataSource) : ISuggestionStore
{
    public async ValueTask<int> InsertAsync(long planId, IReadOnlyList<NewSuggestion> suggestions, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (suggestions.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var written = 0;
        foreach (var s in suggestions)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO workflow.suggestions (plan_id, kind, subject, text, created_at)
                VALUES (@plan, @kind, @subject, @text, @at)
                ON CONFLICT (plan_id, kind, subject) DO NOTHING
                """, connection, transaction);
            insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
            insert.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, s.Kind);
            insert.Parameters.AddWithValue("subject", NpgsqlDbType.Varchar, s.Subject);
            insert.Parameters.AddWithValue("text", NpgsqlDbType.Varchar, Clip(s.Text, NewSuggestion.MaxTextLength));
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
            written += await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    public async ValueTask<IReadOnlyList<StoredSuggestion>> ForPlanAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, plan_id, kind, subject, text, created_at FROM workflow.suggestions WHERE plan_id = @plan ORDER BY id");
        select.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        var rows = new List<StoredSuggestion>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredSuggestion(reader.GetInt64(0), reader.GetInt64(1), new NewSuggestion(reader.GetString(2), reader.GetString(3), reader.GetString(4)), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<long>> SeenIdsAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT s.suggestion_id FROM workflow.suggestion_seen s JOIN workflow.suggestions g ON g.id = s.suggestion_id WHERE g.plan_id = @plan ORDER BY s.suggestion_id");
        select.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        var ids = new List<long>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    public async ValueTask<bool> MarkSeenAsync(long planId, long suggestionId, string actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // One statement: the mark is written only for a suggestion of this plan; the unique index makes a second mark a no-op.
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO workflow.suggestion_seen (suggestion_id, actor, seen_at)
            SELECT g.id, @actor, @at FROM workflow.suggestions g WHERE g.id = @id AND g.plan_id = @plan
            ON CONFLICT (suggestion_id) DO NOTHING
            """);
        insert.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, suggestionId);
        insert.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await using var exists = dataSource.CreateCommand("SELECT 1 FROM workflow.suggestions WHERE id = @id AND plan_id = @plan");
        exists.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, suggestionId);
        exists.Parameters.AddWithValue("plan", NpgsqlDbType.Bigint, planId);
        return await exists.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
