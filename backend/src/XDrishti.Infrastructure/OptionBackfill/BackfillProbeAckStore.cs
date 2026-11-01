using Npgsql;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>The owner's acceptance of the probe result (<c>options.backfill_probe_ack</c>, P14-03): insert-only, the time stamped by the database; no row means not accepted.</summary>
internal sealed class BackfillProbeAckStore(NpgsqlDataSource dataSource) : IBackfillProbeAckStore
{
    private const string Columns = "id, actor, note, accepted_at";

    public async ValueTask<BackfillProbeAck?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM options.backfill_probe_ack ORDER BY id DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<BackfillProbeAck> AppendAsync(string actor, string note, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"INSERT INTO options.backfill_probe_ack (actor, note) VALUES (@actor, @note) RETURNING {Columns}");
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("note", note);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static BackfillProbeAck Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero));
}
