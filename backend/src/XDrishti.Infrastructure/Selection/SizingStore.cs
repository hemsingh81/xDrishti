using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Infrastructure.Selection;

/// <summary>
/// The size scaling's settings and budgets behind <see cref="ISizingStore"/> (spec BK-10): raw SQL, insert and select only. The newest row per key (per basket) is current; the highest settings id is the version. No row means everything OFF and no budgets.
/// Writers are serialised with an advisory lock, so the check "is the newest row still the one the change was judged against" and the insert cannot be interleaved.
/// </summary>
internal sealed class SizingStore(NpgsqlDataSource dataSource) : ISizingStore
{
    private const string SettingsLock = "selection.sizing_settings_changes";

    private const string BudgetLock = "selection.basket_risk_budgets";

    public async ValueTask<SizingState> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT key, new_value, id FROM selection.sizing_settings_changes c
            WHERE id = (SELECT max(id) FROM selection.sizing_settings_changes WHERE key = c.key)
            """);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var settings = SizingSettings.Defaults;
        long version = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            settings = settings.With(reader.GetString(0), reader.GetString(1));
            version = Math.Max(version, reader.GetInt64(2));
        }

        return new SizingState(settings, version);
    }

    public async ValueTask<IReadOnlyList<SizingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, key, old_value, new_value, direction, changed_by, changed_at, hash FROM selection.sizing_settings_changes ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<SizingChangeRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SizingChangeRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, double>> BudgetsAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT basket_id, budget_pct FROM selection.basket_risk_budgets b
            WHERE id = (SELECT max(id) FROM selection.basket_risk_budgets WHERE basket_id = b.basket_id) AND budget_pct IS NOT NULL
            """);
        var budgets = new Dictionary<Guid, double>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            budgets[reader.GetGuid(0)] = (double)reader.GetDecimal(1);
        }

        return budgets;
    }

    public async ValueTask<IReadOnlyList<BudgetChangeRow>> BudgetHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, basket_id, budget_pct, direction, changed_by, changed_at, hash FROM selection.basket_risk_budgets ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<BudgetChangeRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BudgetChangeRow(reader.GetInt64(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : (double)reader.GetDecimal(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6)));
        }

        return rows;
    }

    public async ValueTask<bool> AppendSettingsAsync(IReadOnlyList<SizingChange> rows, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, SettingsLock, cancellationToken);
        await using (var newest = new NpgsqlCommand("SELECT COALESCE(max(id), 0) FROM selection.sizing_settings_changes", connection, transaction))
        {
            if ((long)(await newest.ExecuteScalarAsync(cancellationToken))! != expectedVersion)
            {
                return false;
            }
        }

        foreach (var row in rows)
        {
            await using var insert = new NpgsqlCommand("INSERT INTO selection.sizing_settings_changes (key, old_value, new_value, direction, changed_by, hash) VALUES (@key, @old, @new, @direction, @actor, @hash)", connection, transaction);
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

    public async ValueTask<bool> AppendBudgetAsync(Guid basketId, double? expectedCurrentPct, double? budgetPct, SizingChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, BudgetLock, cancellationToken);
        await using (var newest = new NpgsqlCommand("SELECT budget_pct FROM selection.basket_risk_budgets WHERE basket_id = @basket ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            newest.Parameters.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
            var found = await newest.ExecuteScalarAsync(cancellationToken);
            double? current = found is decimal d ? (double)d : null;
            var changed = (current, expectedCurrentPct) switch
            {
                (null, null) => false,
                ({ } a, { } b) => Math.Abs(a - b) > 0.0001,
                _ => true,
            };
            if (changed)
            {
                return false;
            }
        }

        await using var insert = new NpgsqlCommand("INSERT INTO selection.basket_risk_budgets (basket_id, budget_pct, direction, changed_by, hash) VALUES (@basket, @pct, @direction, @actor, @hash)", connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("basket", NpgsqlDbType.Uuid, basketId);
        p.AddWithValue("pct", NpgsqlDbType.Numeric, budgetPct is { } v ? (decimal)v : DBNull.Value);
        p.AddWithValue("direction", NpgsqlDbType.Varchar, change.Direction);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, change.Actor);
        p.AddWithValue("hash", NpgsqlDbType.Varchar, change.Hash);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, CancellationToken cancellationToken)
    {
        await using var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@name, 0))", connection, transaction);
        advisory.Parameters.AddWithValue("name", NpgsqlDbType.Text, name);
        await advisory.ExecuteNonQueryAsync(cancellationToken);
    }
}
