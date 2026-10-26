using Npgsql;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Infrastructure.Depth;

/// <summary>The owner's start switch of the depth recorder (<c>market.depth_recorder_switch</c>, P7-16): insert-only, the time stamped by the database; no row means OFF.</summary>
internal sealed class DepthSwitchStore(NpgsqlDataSource dataSource) : IDepthSwitchStore
{
    private const string Columns = "id, enabled, actor, reason, changed_at";

    public async ValueTask<DepthSwitchRow?> CurrentAsync(CancellationToken cancellationToken)
    {
        var newest = await HistoryAsync(1, cancellationToken);
        return newest.Count > 0 ? newest[0] : null;
    }

    public async ValueTask<IReadOnlyList<DepthSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM market.depth_recorder_switch ORDER BY id DESC LIMIT @take");
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        var rows = new List<DepthSwitchRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<DepthSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"INSERT INTO market.depth_recorder_switch (enabled, actor, reason) VALUES (@enabled, @actor, @reason) RETURNING {Columns}");
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static DepthSwitchRow Read(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2), reader.GetString(3), new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero));
}
