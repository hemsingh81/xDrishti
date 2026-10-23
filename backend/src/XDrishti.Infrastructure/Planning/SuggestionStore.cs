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

    private static string Clip(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
}
