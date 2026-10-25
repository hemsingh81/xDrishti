using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The learning reports behind <see cref="ILearningReportStore"/> (spec P8-12, ADR 0110): raw SQL, insert and select only (the runtime role has no more). A second insert of the same kind and input hash is skipped by the unique index and the stored row is returned.
/// </summary>
internal sealed class LearningReportStore(NpgsqlDataSource dataSource) : ILearningReportStore
{
    private const string Columns = "id, kind, input_hash, outcome, as_of, basis, sources_json, options_json, report_json, text, built_by, created_at";

    public async ValueTask<(StoredLearningReport Stored, bool Created)> InsertAsync(NewLearningReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO research.learning_reports (kind, input_hash, outcome, as_of, basis, sources_json, options_json, report_json, text, built_by, created_at)
            VALUES (@kind, @hash, @outcome, @asof, @basis, @sources, @options, @report, @text, @by, @at)
            ON CONFLICT (kind, input_hash) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("kind", NpgsqlDbType.Varchar, report.Kind);
        p.AddWithValue("hash", NpgsqlDbType.Varchar, report.InputHash);
        p.AddWithValue("outcome", NpgsqlDbType.Varchar, report.Outcome);
        p.AddWithValue("asof", NpgsqlDbType.Date, report.AsOf);
        p.AddWithValue("basis", NpgsqlDbType.Varchar, report.Basis);
        p.AddWithValue("sources", NpgsqlDbType.Text, report.SourcesJson);
        p.AddWithValue("options", NpgsqlDbType.Text, report.OptionsJson);
        p.AddWithValue("report", NpgsqlDbType.Text, report.ReportJson);
        p.AddWithValue("text", NpgsqlDbType.Text, report.Text);
        p.AddWithValue("by", NpgsqlDbType.Varchar, report.BuiltBy);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, report.CreatedAt.ToUniversalTime());
        var written = await insert.ExecuteNonQueryAsync(cancellationToken);

        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.learning_reports WHERE kind = @kind AND input_hash = @hash");
        select.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, report.Kind);
        select.Parameters.AddWithValue("hash", NpgsqlDbType.Varchar, report.InputHash);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The learning report was not found after it was stored.");
        }

        return (Read(reader), written == 1);
    }

    public async ValueTask<StoredLearningReport?> FindAsync(long id, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.learning_reports WHERE id = @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<StoredLearningReport?> NewestAsync(string kind, CancellationToken cancellationToken)
    {
        var rows = await ListAsync(kind, 1, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<StoredLearningReport>> ListAsync(string kind, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.learning_reports WHERE kind = @kind ORDER BY created_at DESC, id DESC LIMIT @limit");
        select.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, kind);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        var rows = new List<StoredLearningReport>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static StoredLearningReport Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), new NewLearningReport(r.GetString(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateOnly>(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10), r.GetFieldValue<DateTimeOffset>(11)));
}
