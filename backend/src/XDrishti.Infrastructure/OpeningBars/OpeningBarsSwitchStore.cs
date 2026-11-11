using Npgsql;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Infrastructure.OpeningBars;

/// <summary>The owner's stored switch <c>baskets.opening_bars</c> (BK-03b): <c>market.opening_bars_switch</c>, insert-only, the time stamped by the database; the newest row counts and no row means OFF.</summary>
internal sealed class OpeningBarsSwitchStore(NpgsqlDataSource dataSource) : IOpeningBarsSwitchStore
{
    private const string Columns = "id, enabled, actor, reason, changed_at";

    public async ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => (await HistoryAsync(1, cancellationToken)) is [{ Enabled: true }];

    public async ValueTask<IReadOnlyList<OpeningBarsSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM market.opening_bars_switch ORDER BY id DESC LIMIT @take");
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        var rows = new List<OpeningBarsSwitchRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<OpeningBarsSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"INSERT INTO market.opening_bars_switch (enabled, actor, reason) VALUES (@enabled, @actor, @reason) RETURNING {Columns}");
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static OpeningBarsSwitchRow Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
}
