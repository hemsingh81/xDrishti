using Npgsql;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Persistence;

/// <summary>Reads the status and expiry of the account's stored broker connection (three columns, the only ones xd_exec may read) and applies <see cref="TokenGate"/>. A row that is missing or a read that fails is <c>token_unavailable</c>.</summary>
internal sealed class PgTokenGate(NpgsqlDataSource data, TimeProvider clock) : ITokenGate
{
    public async Task<GateDecision> CheckAsync(Guid accountId, int minRemainingMinutes, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT status, expires_at FROM accounts.broker_connections WHERE account_id = @id");
            command.Parameters.AddWithValue("id", accountId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            ConnectionFacts? facts = null;
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                facts = new ConnectionFacts(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
            }

            return TokenGate.Judge(facts, clock.GetUtcNow(), minRemainingMinutes);
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            return new GateDecision(false, "token_unavailable");
        }
    }
}
