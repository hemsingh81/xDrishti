using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.extension_extractions</c> behind <see cref="IExtensionExtractionStore"/> (spec P15-05): raw SQL, select only here; the insert is in the feature store's transaction with the rows.</summary>
internal sealed class ExtensionExtractionStore(NpgsqlDataSource dataSource) : IExtensionExtractionStore
{
    public async ValueTask<ExtensionExtractionRecord?> FindAsync(string runKey, string version, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT run_key, extension, version, lag_minutes, parser_version, calendar_sha256, filings_watermark, rows, nan_counts::text, extracted_at
            FROM research.extension_extractions WHERE run_key = @run_key AND version = @version
            """);
        command.Parameters.AddWithValue("run_key", NpgsqlDbType.Char, runKey);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Varchar, version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ExtensionExtractionRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.GetInt32(7), reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9))
            : null;
    }
}
