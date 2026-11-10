using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Infrastructure.SimAll;

/// <summary>
/// The one reader of <c>training</c> runs (spec AU-12, ADR 0243 point 7, ADR 0248): every statement carries <c>purpose = 'training'</c>, the mirror of the evidence fence, so a research run is invisible here as a training run is invisible to the run store.
/// Not evidence: the architecture tests allow only the learning side to name <see cref="ITrainingRunReader"/>. Headers only for now; trades are added with the first reader that needs them, behind the same fence.
/// </summary>
internal sealed class TrainingRunReader(NpgsqlDataSource dataSource) : ITrainingRunReader
{
    public async ValueTask<IReadOnlyList<TrainingRunHeader>> ListAsync(string definitionSha256, int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT r.run_key, r.strategy_id, r.definition_sha256, r.from_date, r.to_date, r.instruments, r.instruments_run, r.trade_count, r.open_at_end_count, r.created_at FROM research.sim_runs r WHERE r.purpose = 'training' AND r.definition_sha256 = @hash ORDER BY r.created_at, r.id LIMIT @take");
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, definitionSha256);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, Math.Clamp(take, 1, 10_000));
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<TrainingRunHeader?> FindAsync(string runKey, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT r.run_key, r.strategy_id, r.definition_sha256, r.from_date, r.to_date, r.instruments, r.instruments_run, r.trade_count, r.open_at_end_count, r.created_at FROM research.sim_runs r WHERE r.purpose = 'training' AND r.run_key = @key");
        command.Parameters.AddWithValue("key", NpgsqlDbType.Char, runKey);
        return (await ReadAsync(command, cancellationToken)).FirstOrDefault();
    }

    private static async Task<List<TrainingRunHeader>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var list = new List<TrainingRunHeader>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TrainingRunHeader(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return list;
    }
}
