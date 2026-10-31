using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// The global switches as the runtime role (ADR 0174): SELECT on <c>execution.settings</c> and INSERT of the owner's snapshot. The trigger <c>execution.check_settings</c> stamps the time, the hash and the carried-over values and refuses what it
/// must (auto_live ON, a raise sooner than 09:00 India time of the next day); its message comes back as the refusal.
/// </summary>
internal sealed class ExecutionSettingsAccess(XDrishtiDbContext db) : IExecutionSettingsAccess
{
    private const string Prefix = "execution.settings_refused: ";

    public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => DatabaseClock.NowAsync(db, cancellationToken);

    public Task<IReadOnlyList<ExecutionSettingsRowView>> RowsAsync(int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ExecutionSettingsRowView>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT id, execution_enabled, auto_live_enabled, actor, reason, changed_at, effective_from, snapshot_hash FROM execution.settings ORDER BY id DESC LIMIT @n", connection);
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ExecutionSettingsRowView>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ExecutionSettingsRowView(
                    reader.GetInt64(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7).Trim()));
            }

            return rows;
        }, cancellationToken);

    public Task<(long? Id, string? Refused)> InsertAsync(NewExecutionSettings row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecutionConnection.UseAsync<(long?, string?)>(db, async connection =>
        {
            // auto_live_enabled is written OFF by name: this route never sends anything else for it.
            await using var command = new NpgsqlCommand(
                "INSERT INTO execution.settings (execution_enabled, auto_live_enabled, actor, reason, effective_from) VALUES (@on, false, @by, @r, @e) RETURNING id", connection);
            command.Parameters.Add(new NpgsqlParameter("on", NpgsqlDbType.Boolean) { Value = row.ExecutionEnabled });
            command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = row.Actor });
            command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = row.Reason });
            command.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.TimestampTz) { Value = row.EffectiveFrom.ToUniversalTime() });
            try
            {
                return ((long?)await command.ExecuteScalarAsync(cancellationToken), (string?)null);
            }
            catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return (null, failure.MessageText[Prefix.Length..]);
            }
            catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation)
            {
                return (null, "the database refused the row (" + failure.SqlState + ")");
            }
        }, cancellationToken);
    }
}

/// <summary>The drill record and the drill command rows with their answers, read as the runtime role (SELECT only).</summary>
internal sealed class ExecutionDrillReader(XDrishtiDbContext db) : IExecutionDrillReader
{
    public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => DatabaseClock.NowAsync(db, cancellationToken);

    public Task<IReadOnlyList<ExecutionDrillView>> DrillsAsync(Guid? accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ExecutionDrillView>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT id, at, account_id, passed, latency_ms, detail FROM execution.drills WHERE (@a IS NULL OR account_id = @a) ORDER BY at DESC, id DESC LIMIT @n", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ExecutionDrillView>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ExecutionDrillView(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetGuid(2), reader.GetBoolean(3), reader.GetInt32(4), reader.GetString(5)));
            }

            return rows;
        }, cancellationToken);

    public Task<IReadOnlyList<ExecutionDrillRequestView>> RequestsAsync(Guid? accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ExecutionDrillRequestView>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT c.id, c.account_id, c.at, r.result, r.code, r.at
                  FROM execution.commands c LEFT JOIN execution.command_results r ON r.command_id = c.id
                 WHERE c.kind = 'drill' AND (@a IS NULL OR c.account_id = @a)
                 ORDER BY c.at DESC, c.id DESC LIMIT @n
                """, connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ExecutionDrillRequestView>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ExecutionDrillRequestView(
                    reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
            }

            return rows;
        }, cancellationToken);
}
