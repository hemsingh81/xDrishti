using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The loosening notices behind <see cref="ILooseningStore"/> (BK-10b): raw SQL, select and insert only. The loosenings are the rows with direction loosen of the three change tables (read, never written here); the dismissals are
/// <c>trading.loosening_dismissals</c> (one per change, insert-only) and the automatic setting is <c>trading.loosening_settings</c> (insert-only, the id drawn under a lock so the highest id is the last to commit). The old value of a budget is the previous row of the same basket.
/// </summary>
internal sealed class LooseningStore(NpgsqlDataSource dataSource) : ILooseningStore
{
    private const string SettingLock = "trading.loosening_settings";

    private const string SizingSelect = "SELECT 'sizing' AS src, s.id, s.key AS label, NULL::uuid AS basket_id, s.old_value, s.new_value, s.changed_by, s.changed_at, s.direction FROM selection.sizing_settings_changes s";

    private const string BudgetSelect = """
        SELECT 'budget', b.id, 'basket budget ' || COALESCE(k.name, b.basket_id::text), b.basket_id,
               COALESCE((SELECT trim_scale(p.budget_pct)::text || '%' FROM selection.basket_risk_budgets p WHERE p.basket_id = b.basket_id AND p.id < b.id ORDER BY p.id DESC LIMIT 1), 'off'),
               COALESCE(trim_scale(b.budget_pct)::text || '%', 'off'), b.changed_by, b.changed_at, b.direction
        FROM selection.basket_risk_budgets b LEFT JOIN market.baskets k ON k.id = b.basket_id
        """;

    private const string BasketSelect = "SELECT 'basket', x.id, x.key, NULL::uuid, x.old_value, x.new_value, x.changed_by, x.changed_at, x.direction FROM selection.basket_settings_changes x";

    // shortcut: one UNION ALL query over the three change tables with a NOT EXISTS per row; no timing test (BK-10b AC 11) was added. The tables hold a handful of owner changes; upgrade to an indexed view if they ever grow to thousands.
    public async ValueTask<IReadOnlyList<LooseningNotice>> ListUndismissedAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"""
            SELECT n.src, n.id, n.label, n.basket_id, n.old_value, n.new_value, n.changed_by, n.changed_at, n.direction
            FROM ({SizingSelect} UNION ALL {BudgetSelect} UNION ALL {BasketSelect}) n
            WHERE n.direction = '{LooseningDirections.Loosen}'
              AND NOT EXISTS (SELECT 1 FROM trading.loosening_dismissals d WHERE d.notice_source = n.src AND d.notice_id = n.id)
            ORDER BY n.changed_at, n.src, n.id
            """);
        var rows = new List<LooseningNotice>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<LooseningLookup?> FindAsync(string source, long id, CancellationToken cancellationToken)
    {
        var sql = source switch
        {
            LooseningNoticeId.Sizing => SizingSelect + " WHERE s.id = @id",
            LooseningNoticeId.Budget => BudgetSelect + " WHERE b.id = @id",
            LooseningNoticeId.Basket => BasketSelect + " WHERE x.id = @id",
            _ => null,
        };
        if (sql is null)
        {
            return null;
        }

        await using var select = dataSource.CreateCommand(sql);
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, id);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new LooseningLookup(Read(reader), reader.GetString(8)) : null;
    }

    public async ValueTask<bool> AddDismissalAsync(NewLooseningDismissal dismissal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dismissal);
        await using var insert = dataSource.CreateCommand("""
            INSERT INTO trading.loosening_dismissals (notice_source, notice_id, actor, setting_id, setting_hash)
            VALUES (@source, @id, @actor, @setting, @hash)
            ON CONFLICT (notice_source, notice_id) DO NOTHING
            """);
        var p = insert.Parameters;
        p.AddWithValue("source", NpgsqlDbType.Varchar, dismissal.Source);
        p.AddWithValue("id", NpgsqlDbType.Bigint, dismissal.Id);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, dismissal.Actor);
        p.AddWithValue("setting", NpgsqlDbType.Bigint, dismissal.SettingId is { } s ? s : DBNull.Value);
        p.AddWithValue("hash", NpgsqlDbType.Varchar, dismissal.SettingHash is { } h ? h : DBNull.Value);
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<DateTimeOffset?> PreviousPlanCreatedAtAsync(long planId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT max(created_at) FROM trading.plans WHERE id < @id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, planId);
        return await select.ExecuteScalarAsync(cancellationToken) is DateTimeOffset at ? at : null;
    }

    public async ValueTask<LooseningSwitch> CurrentSettingAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, auto_dismiss, hash FROM trading.loosening_settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new LooseningSwitch(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2)) : new LooseningSwitch(null, false, null);
    }

    public async ValueTask<IReadOnlyList<LooseningSettingRow>> SettingHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, auto_dismiss, changed_by, changed_at, hash FROM trading.loosening_settings ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<LooseningSettingRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LooseningSettingRow(reader.GetInt64(0), reader.GetBoolean(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
        }

        return rows;
    }

    public async ValueTask<bool> AppendSettingAsync(bool autoDismiss, string actor, string hash, long expectedVersion, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@name, 0))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("name", NpgsqlDbType.Text, SettingLock);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT COALESCE(max(id), 0) FROM trading.loosening_settings", connection, transaction))
        {
            if ((long)(await newest.ExecuteScalarAsync(cancellationToken))! != expectedVersion)
            {
                return false;
            }
        }

        await using var insert = new NpgsqlCommand("INSERT INTO trading.loosening_settings (auto_dismiss, changed_by, hash) VALUES (@auto, @actor, @hash)", connection, transaction);
        insert.Parameters.AddWithValue("auto", NpgsqlDbType.Boolean, autoDismiss);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        insert.Parameters.AddWithValue("hash", NpgsqlDbType.Varchar, hash);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static LooseningNotice Read(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7));
}
