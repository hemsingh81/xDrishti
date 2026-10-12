using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Quality;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Persistence.Configurations;

namespace XDrishti.Infrastructure.Market.Quality;

/// <summary>
/// Reads of the quality results. The current result of an instrument is the one of the latest run that checked it (a lateral lookup by the (instrument, run) index); the universe is the tracked,
/// not retired instruments that have bars, the same as the checks look at.
/// </summary>
internal sealed class QualityQueries(NpgsqlDataSource dataSource) : IQualityQueries
{
    private const string Universe =
        """
        FROM market.bar_coverage c
        JOIN market.instruments i ON i.id = c.instrument_id AND i.retired_at IS NULL
        JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
        """;

    private static string Latest(string columns) =>
        $"""
        LEFT JOIN LATERAL (
            SELECT {columns}, q.started_at AS checked_at
            FROM market.quality_results r
            JOIN market.quality_runs q ON q.id = r.run_id
            WHERE r.instrument_id = i.id
            ORDER BY q.started_at DESC, q.id DESC
            LIMIT 1) l ON true
        """;

    private const string RunColumns = "id, trigger, status, started_at, finished_at, sessions, instruments, passed, warned, failed, score, error, instrument_id, job_id";

    public async ValueTask<QualityCounts> GetCountsAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // the lateral columns are constants of this class
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT count(*), count(l.status), count(*) FILTER (WHERE l.status = 'pass'), count(*) FILTER (WHERE l.status = 'warning'), count(*) FILTER (WHERE l.status = 'fail'), avg(l.score)::float8
            {Universe}
            {Latest("r.status, r.score")}
            """);
#pragma warning restore CA2100
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var tracked = (int)reader.GetInt64(0);
        var checkedCount = (int)reader.GetInt64(1);
        return new QualityCounts(
            tracked,
            checkedCount,
            (int)reader.GetInt64(2),
            (int)reader.GetInt64(3),
            (int)reader.GetInt64(4),
            tracked - checkedCount,
            reader.IsDBNull(5) ? null : Math.Round(reader.GetDouble(5), 1));
    }

    public async ValueTask<QualityRunView?> GetLastRunAsync(bool allInstruments, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {RunColumns} FROM market.quality_runs WHERE (NOT @all OR instrument_id IS NULL) ORDER BY started_at DESC, id DESC LIMIT 1");
        command.Parameters.AddWithValue("all", allInstruments);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    public async ValueTask<QualityRunView?> GetLastFinishedFullRunAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {RunColumns} FROM market.quality_runs WHERE instrument_id IS NULL AND status <> 'running' ORDER BY started_at DESC, id DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    public async ValueTask<IReadOnlyList<QualityRunView>> ListRunsAsync(int take, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {RunColumns} FROM market.quality_runs ORDER BY started_at DESC, id DESC LIMIT @take");
        command.Parameters.AddWithValue("take", take);
        var runs = new List<QualityRunView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(ReadRun(reader));
        }

        return runs;
    }

    public async ValueTask<QualityInstrumentPage> ListInstrumentsAsync(QualityInstrumentSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        const string Filter =
            """
            WHERE (@pattern IS NULL OR i.canonical_symbol ILIKE @pattern ESCAPE '\' OR i.name ILIKE @pattern ESCAPE '\' OR i.display_name ILIKE @pattern ESCAPE '\')
              AND (@status IS NULL OR (@status = 'notChecked' AND l.status IS NULL) OR l.status = @status)
            """;
        var latest = Latest("r.run_id, r.status, r.score, r.bars_checked, r.missing_minutes, r.checks - 'details' AS checks");
        var order = search.SortField switch
        {
            QualitySortField.Name => "i.name",
            QualitySortField.Status => "CASE l.status WHEN 'fail' THEN 3 WHEN 'warning' THEN 2 WHEN 'pass' THEN 1 ELSE 0 END",
            QualitySortField.Score => "l.score",
            QualitySortField.Coverage => "(100 - (l.checks -> 'completeness' ->> 'missingPercent')::numeric)",
            QualitySortField.BarsChecked => "l.bars_checked",
            QualitySortField.MissingMinutes => "l.missing_minutes",
            QualitySortField.CheckedAt => "l.checked_at",
            _ => "i.canonical_symbol",
        };
        var direction = search.Descending ? "DESC" : "ASC";

        void Bind(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("pattern", NpgsqlDbType.Text, search.Text is null ? DBNull.Value : $"%{LikePattern.Escape(search.Text)}%");
            command.Parameters.AddWithValue("status", NpgsqlDbType.Text, search.Status is null ? DBNull.Value : search.Status);
        }

        int total;
#pragma warning disable CA2100 // the fragments are constants of this class; the ORDER BY comes from a fixed switch; the values are parameters
        await using (var count = dataSource.CreateCommand($"SELECT count(*) {Universe} {latest} {Filter}"))
        {
            Bind(count);
            total = (int)(long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = dataSource.CreateCommand(
            $"""
            SELECT i.id, i.canonical_symbol, i.name, i.type, c.csv_bars, c.dhan_bars, l.run_id, l.status, l.score, l.bars_checked, l.missing_minutes, l.checks::text, l.checked_at
            {Universe}
            {latest}
            {Filter}
            ORDER BY {order} {direction} NULLS LAST, i.canonical_symbol
            OFFSET @offset LIMIT @limit
            """);
#pragma warning restore CA2100
        Bind(command);
        command.Parameters.AddWithValue("offset", (search.Page - 1) * search.PageSize);
        command.Parameters.AddWithValue("limit", search.PageSize);

        var items = new List<QualityInstrumentRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(QualityRows.From(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                SourceOf(reader.GetInt64(4), reader.GetInt64(5)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt16(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                ReadChecks(reader, 11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));
        }

        return new QualityInstrumentPage(items, search.Page, search.PageSize, total);
    }

    public async ValueTask<StoredQualityResult?> FindAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100 // the lateral columns are constants of this class
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT i.id, i.canonical_symbol, i.name, i.type, t.active IS TRUE, c.csv_bars, c.dhan_bars, c.first_bar, c.last_bar,
                   l.run_id, l.checked_at, l.status, l.score, l.bars_checked, l.missing_minutes, l.checks::text
            FROM market.instruments i
            LEFT JOIN market.bar_coverage c ON c.instrument_id = i.id
            LEFT JOIN market.tracked_instruments t ON t.instrument_id = i.id
            {Latest("r.run_id, r.status, r.score, r.bars_checked, r.missing_minutes, r.checks")}
            WHERE i.id = @id
            """);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("id", instrumentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredQualityResult(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : SourceOf(reader.GetInt64(5), reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetInt16(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            ReadChecks(reader, 15));
    }

    private static QualityRunView ReadRun(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(3)),
        reader.IsDBNull(4) ? null : IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(4)),
        reader.GetInt32(5),
        reader.GetInt32(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.GetInt32(9),
        reader.IsDBNull(10) ? null : reader.GetDouble(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetGuid(12),
        reader.IsDBNull(13) ? null : reader.GetGuid(13));

    private static QualityChecks? ReadChecks(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : JsonSerializer.Deserialize<QualityChecks>(reader.GetString(ordinal), DataJobConfiguration.Json);

    private static string? SourceOf(long csv, long dhan) => csv > 0 && dhan > 0 ? "mixed" : dhan > 0 ? "dhan" : csv > 0 ? "csv" : null;
}
