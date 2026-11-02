using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <see cref="ICalendarRows"/> (P15-04, ADR 0181): the stored results rows of the given instruments (or of all) with an intimation time at or before the cutoff <c>t - lag</c>, through the index <c>(instrument_id, kind, broadcast_ts)</c>. The query only narrows; <c>ResultsDays</c>
/// decides. A row without a time is never returned. SELECT only.
/// </summary>
internal sealed class CalendarRows(NpgsqlDataSource dataSource) : ICalendarRows
{
    public async ValueTask<IReadOnlyList<CalendarRow>> ResultsRowsAsync(IReadOnlyCollection<Guid>? instrumentIds, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {FilingRows.Qualified("f")}, c.meeting_date, c.purpose, c.status
            FROM filings.filings f JOIN filings.calendar c ON c.filing_id = f.id
            WHERE f.kind = 'board_meeting' AND c.purpose = 'results' AND f.broadcast_ts <= @cutoff AND (@all OR f.instrument_id = ANY(@ids))
            """);
        command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff.UtcDateTime });
        command.Parameters.AddWithValue("all", instrumentIds is null);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = instrumentIds?.ToArray() ?? [] });
        var rows = new List<CalendarRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CalendarRow(FilingRows.Read(reader), reader.GetFieldValue<DateOnly>(12), reader.GetString(13), reader.GetString(14)));
        }

        return rows;
    }
}
