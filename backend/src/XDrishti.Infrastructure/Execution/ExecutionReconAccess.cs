using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Reads <c>execution.recon_runs</c>, <c>recon_breaks</c> and <c>break_resolutions</c> as the runtime role (SELECT only), newest first.</summary>
internal sealed class ExecutionReconReader(XDrishtiDbContext db) : IExecutionReconReader
{
    private const string BreakSelect =
        """
        SELECT b.id, b.run_id, b.account_id, b.kind, b.severity, b.symbol, b.intent_id, b.detail, b.at, r.at, r.resolved_by, r.resolution, r.note
        FROM execution.recon_breaks b LEFT JOIN execution.break_resolutions r ON r.break_id = b.id
        """;

    public Task<ExecutionReconciliationView> ReadAsync(DateOnly? date, Guid? accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            var runs = new List<ExecutionReconRunView>();
            await using (var command = new NpgsqlCommand("SELECT id, at, account_id, session_date, kind, readable, orders, trades, positions, breaks FROM execution.recon_runs WHERE (@date IS NULL OR session_date = @date) AND (@account IS NULL OR account_id = @account) ORDER BY id DESC LIMIT @take", connection))
            {
                Bind(command, date, accountId, take);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    runs.Add(new ExecutionReconRunView(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetGuid(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetString(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9)));
                }
            }

            var breaks = new List<ExecutionBreakView>();
            await using (var command = new NpgsqlCommand($"{BreakSelect} WHERE (@date IS NULL OR b.session_date = @date) AND (@account IS NULL OR b.account_id = @account) ORDER BY b.id DESC LIMIT @take", connection))
            {
                Bind(command, date, accountId, take);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    breaks.Add(Map(reader));
                }
            }

            return new ExecutionReconciliationView(runs, breaks);
        }, cancellationToken);

    public Task<ExecutionBreakView?> FindBreakAsync(long id, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<ExecutionBreakView?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand($"{BreakSelect} WHERE b.id = @id", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = id });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
        }, cancellationToken);

    private static void Bind(NpgsqlCommand command, DateOnly? date, Guid? accountId, int take)
    {
        command.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date is { } d ? d : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = take });
    }

    private static ExecutionBreakView Map(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.GetString(7), r.GetFieldValue<DateTimeOffset>(8),
        r.IsDBNull(9) ? null : new ExecutionBreakResolution(r.GetFieldValue<DateTimeOffset>(9), r.GetString(10), r.GetString(11), r.GetString(12)));
}

/// <summary>Writes the owner's resolution row (the only INSERT the runtime role holds on the reconciliation tables). The trigger <c>execution.check_break_resolution</c> answers what the database can judge; its message comes back as the refusal.</summary>
internal sealed class ExecutionBreakResolutions(XDrishtiDbContext db) : IExecutionBreakResolutions
{
    private const string Prefix = "execution.resolution_refused: ";

    public Task<string?> ResolveAsync(long breakId, string actor, string resolution, string note, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<string?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand("INSERT INTO execution.break_resolutions (break_id, resolved_by, resolution, note) VALUES (@id, @by, @resolution, @note)", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = breakId });
            command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("resolution", NpgsqlDbType.Varchar) { Value = resolution });
            command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = note });
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
                return "the break is already resolved.";
            }
        }, cancellationToken);
}
