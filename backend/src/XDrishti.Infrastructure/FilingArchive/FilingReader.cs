using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>The metadata reads of the filing store for the two routes (P15-02): counts and envelope fields, never a file, a path or a source reference. SELECT only.</summary>
internal sealed class FilingReader(NpgsqlDataSource dataSource) : IFilingReader
{
    private const string India = "Asia/Kolkata";

    // The reference date of a filing in a coverage range: its period end, else its broadcast date, else the day it was ingested (India time).
    private const string Reference = $"COALESCE(f.period_end, (f.broadcast_ts AT TIME ZONE '{India}')::date, (f.ingested_at AT TIME ZONE '{India}')::date)";

    private const string QuarterEnd = "(date_trunc('quarter', ref) + interval '3 months' - interval '1 day')::date";

    private const string View = "f.id, i.canonical_symbol, f.kind, f.period_end, f.broadcast_ts, f.broadcast_state, f.unavailable_reason, f.ingested_at, f.content_sha256, f.file_bytes, f.file_name, f.supersedes";

    public async ValueTask<FilingCoverageData> CoverageAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var cells = new Dictionary<(string Kind, DateOnly Quarter), Cell>();
        await using (var command = dataSource.CreateCommand(
            $"""
            WITH f AS (SELECT f.id, f.kind, f.broadcast_state, f.unavailable_reason, {Reference} AS ref FROM filings.filings f)
            SELECT kind, {QuarterEnd} AS q, broadcast_state, unavailable_reason, count(*),
                   count(*) FILTER (WHERE EXISTS (SELECT 1 FROM filings.filings s WHERE s.supersedes = f.id))
            FROM f WHERE ref BETWEEN @from AND @to GROUP BY 1, 2, 3, 4
            """))
        {
            Range(command, from, to);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var cell = Get(cells, reader.GetString(0), reader.GetFieldValue<DateOnly>(1));
                var count = (int)reader.GetInt64(4);
                if (reader.GetString(2) == "ok")
                {
                    cell.Ok += count;
                }
                else
                {
                    var reason = reader.IsDBNull(3) ? "missing" : reader.GetString(3);
                    cell.Unavailable[reason] = cell.Unavailable.GetValueOrDefault(reason) + count;
                }

                cell.Superseded += (int)reader.GetInt64(5);
            }
        }

        await using (var command = dataSource.CreateCommand(
            $"""
            WITH roots AS (
                SELECT f.kind, f.instrument_id, f.period_end AS ref, count(*) AS n
                FROM filings.filings f
                WHERE f.period_end IS NOT NULL AND f.broadcast_state = 'ok' AND f.supersedes IS NULL AND f.period_end BETWEEN @from AND @to
                GROUP BY f.kind, f.instrument_id, f.period_end)
            SELECT kind, {QuarterEnd} AS q, sum(n - 1)::bigint FROM roots WHERE n > 1 GROUP BY 1, 2
            """))
        {
            Range(command, from, to);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Get(cells, reader.GetString(0), reader.GetFieldValue<DateOnly>(1)).OutOfOrder += (int)reader.GetInt64(2);
            }
        }

        var tracked = 0;
        await using (var command = dataSource.CreateCommand($"SELECT count(*) FROM market.instruments WHERE {FilingInstruments.Tracked}"))
        {
            tracked = (int)Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        var without = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = dataSource.CreateCommand(
            $"""
            WITH f AS (SELECT f.kind, f.instrument_id, {Reference} AS ref FROM filings.filings f)
            SELECT f.kind, count(DISTINCT f.instrument_id) FROM f JOIN market.instruments i ON i.id = f.instrument_id AND i.type IN ('share', 'etf') AND i.retired_at IS NULL
            WHERE f.ref BETWEEN @from AND @to GROUP BY f.kind
            """))
        {
            Range(command, from, to);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                without[reader.GetString(0)] = Math.Max(0, tracked - (int)reader.GetInt64(1));
            }
        }

        return new FilingCoverageData(
            [.. cells.Select(c => new FilingCoverageCell(c.Key.Kind, c.Key.Quarter, c.Value.Ok, c.Value.Superseded, c.Value.OutOfOrder, c.Value.Unavailable))],
            tracked,
            without);
    }

    public async ValueTask<IReadOnlyList<FilingView>> ListAsync(Guid? instrumentId, FilingKind? kind, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {View} FROM filings.filings f JOIN market.instruments i ON i.id = f.instrument_id
            WHERE (@i IS NULL OR f.instrument_id = @i) AND (@k IS NULL OR f.kind = @k)
            ORDER BY (f.broadcast_ts IS NULL), f.broadcast_ts DESC, f.id
            LIMIT @limit
            """);
        command.Parameters.Add(new NpgsqlParameter("i", NpgsqlDbType.Uuid) { Value = instrumentId is { } id ? id : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Varchar) { Value = kind is { } k ? FilingKinds.Code(k) : DBNull.Value });
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<FilingView>> ByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand($"SELECT {View} FROM filings.filings f JOIN market.instruments i ON i.id = f.instrument_id WHERE f.id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", ids.ToArray());
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<FilingView?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await ByIdsAsync([id], cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static void Range(NpgsqlCommand command, DateOnly from, DateOnly to)
    {
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
    }

    private static Cell Get(Dictionary<(string, DateOnly), Cell> cells, string kind, DateOnly quarter)
    {
        if (!cells.TryGetValue((kind, quarter), out var cell))
        {
            cells[(kind, quarter)] = cell = new Cell();
        }

        return cell;
    }

    private static async ValueTask<IReadOnlyList<FilingView>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<FilingView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FilingView(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11)));
        }

        return rows;
    }

    private sealed class Cell
    {
        public int Ok { get; set; }

        public int Superseded { get; set; }

        public int OutOfOrder { get; set; }

        public Dictionary<string, int> Unavailable { get; } = new(StringComparer.Ordinal);
    }
}
