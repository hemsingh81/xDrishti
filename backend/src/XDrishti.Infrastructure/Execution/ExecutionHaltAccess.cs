using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Opens the context's connection for one statement (and closes it again only if this call opened it).</summary>
internal static class ExecutionConnection
{
    public static async Task<T> UseAsync<T>(XDrishtiDbContext db, Func<NpgsqlConnection, Task<T>> work, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await work(connection);
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}

/// <summary>Reads <c>execution.halts</c> with its two ends as the runtime role (SELECT only), newest first.</summary>
internal sealed class ExecutionHaltReader(XDrishtiDbContext db) : IExecutionHaltReader
{
    private const string Select =
        """
        SELECT h.id, h.at, h.scope, h.account_id, h.level, h.trigger_code, h.source, h.actor, h.reason,
               COALESCE(r.at, c.at), CASE WHEN r.halt_id IS NOT NULL THEN r.released_by WHEN c.halt_id IS NOT NULL THEN 'system:halts' END,
               CASE WHEN r.halt_id IS NOT NULL THEN 'owner' WHEN c.halt_id IS NOT NULL THEN 'auto' END, r.reason,
               EXISTS (SELECT 1 FROM execution.positions p WHERE p.net_qty <> 0 AND (h.scope = 'system' OR p.account_id = h.account_id)),
               EXISTS (SELECT 1 FROM execution.alarms a WHERE a.code IN ('protection_breach', 'flatten_incomplete', 'oversold', 'protection_unknown')
                       AND NOT EXISTS (SELECT 1 FROM execution.alarm_clears k WHERE k.alarm_id = a.id) AND (h.scope = 'system' OR a.account_id IS NULL OR a.account_id = h.account_id))
        FROM execution.halts h
        LEFT JOIN execution.halt_releases r ON r.halt_id = h.id
        LEFT JOIN execution.halt_auto_clears c ON c.halt_id = h.id
        """;

    public Task<IReadOnlyList<ExecutionHaltView>> ReadAsync(bool active, Guid? accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ExecutionHaltView>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand($"{Select} WHERE (@active = false OR (r.halt_id IS NULL AND c.halt_id IS NULL)) AND (@account IS NULL OR h.account_id = @account) ORDER BY h.id DESC LIMIT @take", connection);
            command.Parameters.Add(new NpgsqlParameter("active", NpgsqlDbType.Boolean) { Value = active });
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ExecutionHaltView>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(Map(reader));
            }

            return rows;
        }, cancellationToken);

    public Task<HaltReleaseFacts?> FindAsync(long id, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<HaltReleaseFacts?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand($"{Select} WHERE h.id = @id", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? new HaltReleaseFacts(Map(reader), reader.GetBoolean(13), reader.GetBoolean(14)) : null;
        }, cancellationToken);

    private static ExecutionHaltView Map(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetGuid(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8),
        r.IsDBNull(9) ? null : new ExecutionHaltEnd(r.GetFieldValue<DateTimeOffset>(9), r.GetString(10), r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12)));
}

/// <summary>Leaves a command row for the execution service. The runtime role has INSERT on <c>execution.commands</c> and nothing else there; the table's check refuses a payload with a secret-like word and an actor that is not owner or system.</summary>
internal sealed class ExecutionCommands(XDrishtiDbContext db) : IExecutionCommands
{
    public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            var id = Guid.CreateVersion7();
            await using var command = new NpgsqlCommand("INSERT INTO execution.commands (id, kind, account_id, payload, actor) VALUES (@id, @kind, @account, @payload, @actor)", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
            command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Varchar) { Value = kind });
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payloadJson });
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Varchar) { Value = actor });
            await command.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }, cancellationToken);
}

/// <summary>Writes the owner's release row (the only INSERT the runtime role holds on a halt table). The trigger <c>execution.check_halt_release</c> answers what the database can judge; its message comes back as the refusal.</summary>
internal sealed class ExecutionHaltReleases(XDrishtiDbContext db) : IExecutionHaltReleases
{
    private const string Prefix = "execution.release_refused: ";

    public Task<string?> ReleaseAsync(long haltId, string actor, string reason, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<string?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand("INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES (@id, @by, @reason)", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = haltId });
            command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = reason });
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                return null;
            }
            catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return failure.MessageText[Prefix.Length..];
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return "the halt is already released.";
            }
        }, cancellationToken);
}
