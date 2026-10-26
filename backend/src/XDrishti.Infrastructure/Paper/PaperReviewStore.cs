using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The weekly paper reviews behind <see cref="IPaperReviewStore"/> (spec P10-03, ADR 0121): raw SQL, insert and select only (the runtime role has no more). A week and epoch has one review: the second insert is skipped by the unique index
/// (<c>ON CONFLICT DO NOTHING</c>) and the stored one is returned. Nothing here talks to a broker.
/// </summary>
internal sealed class PaperReviewStore(NpgsqlDataSource dataSource) : IPaperReviewStore
{
    private const string Columns = "id, iso_week, epoch_id, built_at, built_by, text, sections";

    public async ValueTask<(StoredPaperReview Stored, bool Created)> InsertAsync(NewPaperReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO paper.weekly_reviews (iso_week, epoch_id, built_at, built_by, text, sections)
            VALUES (@week, @epoch, @at, @by, @text, @sections)
            ON CONFLICT (iso_week, epoch_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("week", NpgsqlDbType.Text, review.Week);
        p.AddWithValue("epoch", NpgsqlDbType.Bigint, review.EpochId);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, review.BuiltAt.ToUniversalTime());
        p.AddWithValue("by", NpgsqlDbType.Text, review.BuiltBy);
        p.AddWithValue("text", NpgsqlDbType.Text, review.Text);
        p.AddWithValue("sections", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(review.Sections));
        var written = await insert.ExecuteNonQueryAsync(cancellationToken);
        var stored = await FindAsync(review.Week, review.EpochId, cancellationToken) ?? throw new InvalidOperationException("The review was not found after it was stored.");
        return (stored, written == 1);
    }

    public async ValueTask<StoredPaperReview?> FindAsync(string isoWeek, long epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM paper.weekly_reviews WHERE iso_week = @week AND epoch_id = @epoch");
        select.Parameters.AddWithValue("week", NpgsqlDbType.Text, isoWeek);
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredPaperReview>> ListAsync(long? epochId, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM paper.weekly_reviews WHERE (@epoch::bigint IS NULL OR epoch_id = @epoch::bigint) ORDER BY iso_week DESC, id DESC LIMIT @limit");
        select.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = epochId is { } e ? e : DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var rows = new List<StoredPaperReview>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static StoredPaperReview Read(NpgsqlDataReader reader)
    {
        var sections = JsonSerializer.Deserialize<List<ReviewSection>>(reader.GetString(6)) ?? [];
        return new StoredPaperReview(reader.GetInt64(0), new NewPaperReview(reader.GetString(1), reader.GetInt64(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4), reader.GetString(5), sections));
    }
}
