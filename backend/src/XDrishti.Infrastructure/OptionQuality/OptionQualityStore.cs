using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Infrastructure.OptionQuality;

/// <summary>
/// <c>options.quality_runs</c> and <c>options.quality_results</c> (P14-04, ADR 0178). Insert-only for the runtime role: a run's id is taken from the sequence when it starts, its results are copied in per session, and the run row is inserted when it ends. A re-run writes new
/// rows; the reads take the newest run per session and check. Nothing here reads or writes an archive table.
/// </summary>
internal sealed class OptionQualityStore(NpgsqlDataSource dataSource) : IOptionQualityStore
{
    private const string Columns = "session_date, underlying_id, source, expiry, fetch_key, check_code, status, reason, value, threshold, n, detail::text";

    private const string Newest =
        $"""
        SELECT DISTINCT ON (session_date, source, COALESCE(fetch_key, ''), check_code) {Columns}
        FROM options.quality_results
        """;

    public async ValueTask<long> StartRunAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT nextval('options.quality_runs_id_seq')");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask AddResultsAsync(long runId, IReadOnlyList<OptionQualityRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY options.quality_results (run_id, session_date, underlying_id, source, expiry, fetch_key, check_code, status, reason, value, threshold, n, detail) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var row in rows)
        {
            var check = row.Check;
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(runId, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(row.SessionDate, NpgsqlDbType.Date, cancellationToken);
            await importer.WriteAsync(row.UnderlyingId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.Source, NpgsqlDbType.Varchar, cancellationToken);
            await Nullable(importer, row.Expiry, NpgsqlDbType.Date, cancellationToken);
            await Nullable(importer, row.FetchKey, NpgsqlDbType.Varchar, cancellationToken);
            await importer.WriteAsync(check.Code, NpgsqlDbType.Varchar, cancellationToken);
            await importer.WriteAsync(check.Status.Code(), NpgsqlDbType.Varchar, cancellationToken);
            await Nullable(importer, check.Reason, NpgsqlDbType.Varchar, cancellationToken);
            await Nullable(importer, check.Value is { } value ? (decimal?)value : null, NpgsqlDbType.Numeric, cancellationToken);
            await Nullable(importer, check.Threshold is { } threshold ? (decimal?)threshold : null, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(check.N, NpgsqlDbType.Integer, cancellationToken);
            await Nullable(importer, check.Detail, NpgsqlDbType.Jsonb, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    public async ValueTask FinishRunAsync(long runId, string trigger, DateTimeOffset startedAt, DateTimeOffset endedAt, int sessions, int results, string? note, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO options.quality_runs (id, trigger, started_at, ended_at, sessions, results, note) VALUES (@id, @trigger, @started, @ended, @sessions, @results, @note)");
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("trigger", trigger);
        command.Parameters.AddWithValue("started", startedAt.UtcDateTime);
        command.Parameters.AddWithValue("ended", endedAt.UtcDateTime);
        command.Parameters.AddWithValue("sessions", sessions);
        command.Parameters.AddWithValue("results", results);
        command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = (object?)note ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OptionQualityRow>> ChainResultsAsync(Guid underlyingId, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(Newest + " WHERE underlying_id = @u AND source = 'chain' AND session_date <= @to ORDER BY session_date, source, COALESCE(fetch_key, ''), check_code, run_id DESC");
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("to", to);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OptionQualityRow>> RollingResultsAsync(Guid underlyingId, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        // The limit counts key-sessions (a day of one fetch key), not rows: a key-session has two checks and must never be cut in half.
        await using var command = dataSource.CreateCommand(
            $"""
            WITH newest AS ({Newest} WHERE underlying_id = @u AND source = 'rolling' AND session_date BETWEEN @from AND @to ORDER BY session_date, source, COALESCE(fetch_key, ''), check_code, run_id DESC),
                 keys AS (SELECT DISTINCT session_date, fetch_key FROM newest ORDER BY session_date, fetch_key LIMIT @limit)
            SELECT n.session_date, n.underlying_id, n.source, n.expiry, n.fetch_key, n.check_code, n.status, n.reason, n.value, n.threshold, n.n, n.detail
            FROM newest n JOIN keys k ON k.session_date = n.session_date AND k.fetch_key = n.fetch_key
            ORDER BY n.session_date, n.fetch_key, n.check_code
            """);
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAsync(command, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<OptionQualityRow>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<OptionQualityRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = QualityStatuses.TryParse(reader.GetString(6), out var parsed) ? parsed : QualityStatus.Unavailable;
            var check = new QualityCheck(
                reader.GetString(5),
                status,
                reader.IsDBNull(8) ? null : (double)reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : (double)reader.GetDecimal(9),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11));
            rows.Add(new OptionQualityRow(reader.GetFieldValue<DateOnly>(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3), reader.IsDBNull(4) ? null : reader.GetString(4), check));
        }

        return rows;
    }

    private static async ValueTask Nullable<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is { } present)
        {
            await importer.WriteAsync(present, type, cancellationToken);
        }
        else
        {
            await importer.WriteNullAsync(cancellationToken);
        }
    }

    private static async ValueTask Nullable(NpgsqlBinaryImporter importer, string? value, NpgsqlDbType type, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value, type, cancellationToken);
        }
    }
}
