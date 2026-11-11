using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Infrastructure.Selection;

/// <summary>
/// The basket settings behind <see cref="IBasketSettingsStore"/> (spec BK-08): <c>selection.basket_settings_changes</c>, raw SQL, insert and select only. The newest row per key is current; the highest id is the version. No row means everything OFF.
/// Writers are serialised with an advisory lock, so "is the newest row still the one the change was judged against" and the insert cannot be interleaved.
/// </summary>
internal sealed class BasketSettingsStore(NpgsqlDataSource dataSource) : IBasketSettingsStore
{
    private const string WriteLock = "selection.basket_settings_changes";

    public async ValueTask<BasketSettingsState> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT key, new_value, id FROM selection.basket_settings_changes c
            WHERE id = (SELECT max(id) FROM selection.basket_settings_changes WHERE key = c.key)
            """);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var settings = BasketSettings.Defaults;
        long version = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            settings = settings.With(reader.GetString(0), reader.GetString(1));
            version = Math.Max(version, reader.GetInt64(2));
        }

        return new BasketSettingsState(settings, version);
    }

    public async ValueTask<IReadOnlyList<BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, key, old_value, new_value, direction, changed_by, changed_at, hash FROM selection.basket_settings_changes ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<BasketSettingChangeRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BasketSettingChangeRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7)));
        }

        return rows;
    }

    public async ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@name, 0))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("name", NpgsqlDbType.Text, WriteLock);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT COALESCE(max(id), 0) FROM selection.basket_settings_changes", connection, transaction))
        {
            if ((long)(await newest.ExecuteScalarAsync(cancellationToken))! != expectedVersion)
            {
                return false;
            }
        }

        foreach (var row in rows)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO selection.basket_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES (@key, @old, @new, @direction, @actor, @hash)", connection, transaction);
            var p = insert.Parameters;
            p.AddWithValue("key", NpgsqlDbType.Varchar, row.Key);
            p.AddWithValue("old", NpgsqlDbType.Varchar, row.OldValue);
            p.AddWithValue("new", NpgsqlDbType.Varchar, row.NewValue);
            p.AddWithValue("direction", NpgsqlDbType.Varchar, row.Direction);
            p.AddWithValue("actor", NpgsqlDbType.Varchar, row.Actor);
            p.AddWithValue("hash", NpgsqlDbType.Varchar, row.Hash);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
