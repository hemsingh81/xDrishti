using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.universe_settings_changes</c> behind <see cref="IUniverseSettingsStore"/> (BK-11): raw SQL, insert and select only. The newest row of a key is its value; a key with no row has its default.</summary>
internal sealed class UniverseSettingsStore(NpgsqlDataSource dataSource) : IUniverseSettingsStore
{
    public async ValueTask<UniverseSettingsState> CurrentAsync(CancellationToken cancellationToken)
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var newest = dataSource.CreateCommand("SELECT DISTINCT ON (key) key, new_value FROM research.universe_settings_changes ORDER BY key, id DESC"))
        await using (var reader = await newest.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                current[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var history = new List<StoredUniverseSettingChange>();
        await using (var recent = dataSource.CreateCommand("SELECT id, key, old_value, new_value, changed_by, changed_at, hash FROM research.universe_settings_changes ORDER BY id DESC LIMIT 50"))
        await using (var reader = await recent.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new StoredUniverseSettingChange(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6)));
            }
        }

        var defaults = UniverseSettings.Defaults;
        var settings = new UniverseSettings(
            Int(current, UniverseSettingsRules.MinTradesChildKey, defaults.MinTradesChild),
            Int(current, UniverseSettingsRules.LinkMinTradesKey, defaults.LinkMinTrades),
            current.TryGetValue(UniverseSettingsRules.RegisteredBasketsKey, out var list) ? UniverseSettingsRules.ParseList(list) : defaults.RegisteredBaskets);
        return new UniverseSettingsState(settings, history, history.Count == 0 ? 0 : history[0].Id);
    }

    public async ValueTask<bool> AppendAsync(IReadOnlyList<UniverseSettingChange> changes, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('research.universe_settings_changes'))", connection, transaction))
        {
            await gate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT coalesce(max(id), 0) FROM research.universe_settings_changes", connection, transaction))
        {
            if (Convert.ToInt64(await newest.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != expectedVersion)
            {
                return false;
            }
        }

        foreach (var c in changes)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO research.universe_settings_changes (key, old_value, new_value, changed_by, hash) VALUES (@key, @old, @new, @by, @hash)", connection, transaction);
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Varchar, c.Key);
            insert.Parameters.AddWithValue("old", NpgsqlDbType.Text, c.OldValue);
            insert.Parameters.AddWithValue("new", NpgsqlDbType.Text, c.NewValue);
            insert.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, c.Actor);
            insert.Parameters.AddWithValue("hash", NpgsqlDbType.Varchar, c.Hash);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static int Int(Dictionary<string, string> current, string key, int fallback) =>
        current.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : fallback;
}
