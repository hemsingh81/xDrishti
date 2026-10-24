using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The weekly digests behind <see cref="IDigestStore"/> (spec P7-15, ADR 0094): raw SQL, insert and select only (the runtime role has no more). A week has one digest: the second insert is skipped by the unique index
/// (<c>ON CONFLICT DO NOTHING</c>) and the stored one is returned. Nothing here talks to a broker.
/// </summary>
internal sealed class DigestStore(NpgsqlDataSource dataSource) : IDigestStore
{
    private const string Columns = "id, iso_week, week_start, week_end, text, sections_json, built_by, created_at";

    public async ValueTask<(StoredDigest Stored, bool Created)> InsertAsync(NewDigest digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var d = digest.Digest;
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO research.digests (iso_week, week_start, week_end, text, sections_json, built_by, created_at)
            VALUES (@week, @start, @end, @text, @sections, @by, @at)
            ON CONFLICT (iso_week) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("week", NpgsqlDbType.Varchar, d.Week.Key);
        p.AddWithValue("start", NpgsqlDbType.Date, d.Week.Start);
        p.AddWithValue("end", NpgsqlDbType.Date, d.Week.End);
        p.AddWithValue("text", NpgsqlDbType.Text, d.Text);
        p.AddWithValue("sections", NpgsqlDbType.Text, JsonSerializer.Serialize(d.Sections));
        p.AddWithValue("by", NpgsqlDbType.Varchar, digest.BuiltBy);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, digest.CreatedAt.ToUniversalTime());
        var written = await insert.ExecuteNonQueryAsync(cancellationToken);
        var stored = await FindByWeekAsync(d.Week.Key, cancellationToken) ?? throw new InvalidOperationException("The digest was not found after it was stored.");
        return (stored, written == 1);
    }

    public async ValueTask<StoredDigest?> FindByWeekAsync(string isoWeek, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.digests WHERE iso_week = @week");
        select.Parameters.AddWithValue("week", NpgsqlDbType.Varchar, isoWeek);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredDigest>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.digests ORDER BY week_start DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var rows = new List<StoredDigest>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static StoredDigest Read(NpgsqlDataReader reader)
    {
        var week = DigestWeek.TryParse(reader.GetString(1)) ?? throw new InvalidOperationException("A stored digest has a week that is not valid.");
        var sections = JsonSerializer.Deserialize<List<DigestSection>>(reader.GetString(5)) ?? [];
        return new StoredDigest(reader.GetInt64(0), new WeeklyDigest(week, sections, reader.GetString(4)), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7));
    }
}
