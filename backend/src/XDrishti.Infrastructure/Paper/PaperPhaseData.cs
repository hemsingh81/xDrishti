using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The raw read behind the paper phase check (spec P10-06, ADR 0123): one select over the capture rows. The class inserts nothing.</summary>
/// <remarks><c>paper.capture_runs</c> has no epoch column, so the window (the epoch's sessions) is the only filter: a row written for a session that two epochs share counts in both.</remarks>
internal sealed class PaperPhaseData(NpgsqlDataSource dataSource) : IPaperPhaseData
{
    public async ValueTask<IReadOnlyList<CaptureRunView>> CaptureRunsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT id, session_date, started_at, finished_at, outcome, seen, entries_written, exits_written, skips_written, pending, integrity, manual, reason
            FROM paper.capture_runs WHERE session_date >= @from AND session_date <= @to ORDER BY session_date, id
            """);
        select.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        select.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        var items = new List<CaptureRunView>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CaptureRunView(
                reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetBoolean(11), reader.GetString(12)));
        }

        return items;
    }
}
