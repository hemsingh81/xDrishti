using Npgsql;
using XDrishti.Execution.Application;

namespace XDrishti.Execution.Persistence;

/// <summary>The command plane in the database: reads <c>execution.commands</c> that have no <c>command_results</c> row, and writes the one answer. The primary key of the answer table makes a second answer impossible.</summary>
internal sealed class PgExecCommandStore(NpgsqlDataSource data) : IExecCommandStore
{
    public async Task<IReadOnlyList<ExecCommand>> PendingAsync(int max, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("""
            SELECT c.id, c.kind, c.account_id, c.payload::text, c.actor, c.at
            FROM execution.commands c
            WHERE NOT EXISTS (SELECT 1 FROM execution.command_results r WHERE r.command_id = c.id)
            ORDER BY c.at, c.id
            LIMIT @max
            """);
        command.Parameters.AddWithValue("max", max);
        var found = new List<ExecCommand>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new ExecCommand(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return found;
    }

    public async Task<bool> AnswerAsync(Guid commandId, string result, string code, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("INSERT INTO execution.command_results (command_id, result, code) VALUES (@id, @result, @code) ON CONFLICT (command_id) DO NOTHING");
        command.Parameters.AddWithValue("id", commandId);
        command.Parameters.AddWithValue("result", result);
        command.Parameters.AddWithValue("code", code);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
