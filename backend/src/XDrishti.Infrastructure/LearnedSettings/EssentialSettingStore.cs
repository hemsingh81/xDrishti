using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.SettingsCatalog;

namespace XDrishti.Infrastructure.LearnedSettings;

/// <summary>The Essential settings that are one short text, in <c>platform.app_settings</c> (ADR 0002): a read, and an upsert that records who and when. The module that owns a key checks its values; the handlers of the Settings catalog do.</summary>
internal sealed class EssentialSettingStore(NpgsqlDataSource dataSource) : IEssentialSettingStore
{
    public async ValueTask<StoredEssential?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT value, updated_by, updated_at FROM platform.app_settings WHERE key = @key");
        command.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StoredEssential(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)) : null;
    }

    public async ValueTask SetAsync(string key, string value, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES (@key, @value, @by, @at)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_by = EXCLUDED.updated_by, updated_at = EXCLUDED.updated_at
            """);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, key);
        command.Parameters.AddWithValue("value", NpgsqlDbType.Varchar, value);
        command.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
