using Npgsql;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>The owner's start switch of the expired-options backfill (<c>options.backfill_switch</c>, P14-03): insert-only, the time stamped by the database; no row means OFF.</summary>
internal sealed class OptionBackfillSwitchStore(NpgsqlDataSource dataSource) : IOptionBackfillSwitchStore
{
    private const string Columns = "id, enabled, actor, reason, changed_at";

    public async ValueTask<BackfillSwitchRow?> CurrentAsync(CancellationToken cancellationToken)
    {
        var newest = await HistoryAsync(1, cancellationToken);
        return newest.Count > 0 ? newest[0] : null;
    }

    public async ValueTask<IReadOnlyList<BackfillSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM options.backfill_switch ORDER BY id DESC LIMIT @take");
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        var rows = new List<BackfillSwitchRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<BackfillSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"INSERT INTO options.backfill_switch (enabled, actor, reason) VALUES (@enabled, @actor, @reason) RETURNING {Columns}");
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static BackfillSwitchRow Read(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2), reader.GetString(3), new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero));
}
