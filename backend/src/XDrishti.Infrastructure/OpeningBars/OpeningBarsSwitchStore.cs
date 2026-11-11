using Npgsql;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Infrastructure.OpeningBars;

/// <summary>The owner's stored switch <c>baskets.opening_bars</c> (BK-03b): <c>market.opening_bars_switch</c>, insert-only, the time stamped by the database; the newest row counts and no row means OFF.</summary>
internal sealed class OpeningBarsSwitchStore(NpgsqlDataSource dataSource) : IOpeningBarsSwitchStore
{
    public async ValueTask<bool> IsOnAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT enabled FROM market.opening_bars_switch ORDER BY id DESC LIMIT 1");
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async ValueTask AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO market.opening_bars_switch (enabled, actor, reason) VALUES (@enabled, @actor, @reason)");
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
