using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Infrastructure.Confidence;

/// <summary><c>confidence.decay_checks</c> behind <see cref="IDecayCheckStore"/> (spec CF-12 part 12b): one insert-only evaluation per strategy and India day, with the settings hash. A second insert for the same day does nothing.</summary>
internal sealed class DecayChecks(NpgsqlDataSource dataSource) : IDecayCheckStore
{
    public async ValueTask<bool> ExistsAsync(string strategy, DateOnly day, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT 1 FROM confidence.decay_checks WHERE strategy = @strategy AND checked_on = @day");
        select.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, strategy);
        select.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        return await select.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<bool> InsertAsync(NewDecayCheck check, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO confidence.decay_checks (strategy, epoch_id, checked_on, settings_hash, edge_state, trades, verdicts)
            VALUES (@strategy, @epoch, @day, @hash, @state, @trades, @verdicts)
            ON CONFLICT (strategy, checked_on) DO NOTHING
            """);
        command.Parameters.AddWithValue("strategy", NpgsqlDbType.Varchar, check.Strategy);
        command.Parameters.Add(new NpgsqlParameter("epoch", NpgsqlDbType.Bigint) { Value = (object?)check.EpochId ?? DBNull.Value });
        command.Parameters.AddWithValue("day", NpgsqlDbType.Date, day);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, check.SettingsHash);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Varchar, check.EdgeState);
        command.Parameters.AddWithValue("trades", NpgsqlDbType.Integer, check.Trades);
        command.Parameters.AddWithValue("verdicts", NpgsqlDbType.Jsonb, check.Verdicts);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
