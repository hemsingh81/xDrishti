using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Selection;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <see cref="IEventFilterDecisions"/> (P15-06, ADR 0182): the decision history of the event-risk switch as the runtime role. SELECT and INSERT on <c>filings.event_filter_decisions</c> only (the table is insert-only for <c>xd_app</c>; nothing here updates or deletes). The trigger
/// stamps the time and holds the automatic path to its limits; the check constraints judge the owner and auto shapes. A refusal of the database comes back as its text. The id and the time are the database's.
/// </summary>
internal sealed class EventFilterDecisions(NpgsqlDataSource dataSource) : IEventFilterDecisions
{
    private const string Prefix = "filings.event_filter_refused: ";

    public async Task<IReadOnlyList<EventFilterRow>> RowsAsync(int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT id, enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot::text, verdict_ref, changed_at FROM filings.event_filter_decisions ORDER BY id DESC LIMIT @n");
        command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = Math.Clamp(take, 1, 500) });
        var rows = new List<EventFilterRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EventFilterRow(
                reader.GetInt64(0), reader.GetBoolean(1), reader.GetInt16(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return rows;
    }

    public async Task<(long? Id, string? Refused)> AppendAsync(NewEventFilterRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var command = dataSource.CreateCommand(
            "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot, verdict_ref) VALUES (@e, @d, @a, @by, @actor, @r, @s, @v) RETURNING id");
        command.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.Boolean) { Value = row.Enabled });
        command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Smallint) { Value = (short)row.ResultsWithinDays });
        command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Varchar) { Value = row.Approval });
        command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = row.DecidedBy });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Varchar) { Value = row.Actor });
        command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = row.Reason });
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Jsonb) { Value = row.SettingSnapshot });
        command.Parameters.Add(new NpgsqlParameter("v", NpgsqlDbType.Varchar) { Value = (object?)row.VerdictRef ?? DBNull.Value });
        try
        {
            return ((long?)await command.ExecuteScalarAsync(cancellationToken), null);
        }
        catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return (null, failure.MessageText[Prefix.Length..]);
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation)
        {
            return (null, "the database refused the row (" + failure.SqlState + ")");
        }
    }
}
