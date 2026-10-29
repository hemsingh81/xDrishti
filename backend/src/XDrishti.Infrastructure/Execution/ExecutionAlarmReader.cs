using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Reads <c>execution.alarms</c> and <c>execution.alarm_clears</c> as the runtime role (SELECT only), newest first. Plain typed parameters, so a missing account filter is a typed null.</summary>
internal sealed class ExecutionAlarmReader(XDrishtiDbContext db) : IExecutionAlarmReader
{
    private const string Sql =
        """
        SELECT a.id, a.at, a.code, a.severity, a.account_id, i.public_id, a.detail, c.at, c.cleared_by
        FROM execution.alarms a
        LEFT JOIN execution.alarm_clears c ON c.alarm_id = a.id
        LEFT JOIN execution.intents i ON i.id = a.intent_id
        WHERE (@open = false OR c.alarm_id IS NULL) AND (@account IS NULL OR a.account_id = @account)
        ORDER BY a.id DESC
        LIMIT @take
        """;

    public async Task<IReadOnlyList<ExecutionAlarmView>> ReadAsync(bool open, Guid? accountId, int take, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(Sql, connection);
            command.Parameters.Add(new NpgsqlParameter("open", NpgsqlDbType.Boolean) { Value = open });
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ExecutionAlarmView>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ExecutionAlarmView(
                    reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
            }

            return rows;
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
