using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.holdout_blocks</c>, <c>lab.holdout_releases</c> and <c>lab.holdout_unlocks</c> behind <see cref="IHoldOutStore"/> (CF-06, ADR 0207; HO-01, ADR 0283): raw SQL, insert and select only. The only code that names the block and release tables (an architecture test pins it). No market data is read here.</summary>
internal sealed class HoldOutStore(NpgsqlDataSource dataSource) : IHoldOutStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string BlockColumns = "b.start_date, b.history_first, b.history_last, b.length_months, b.setting_hash, b.created_at, b.prev_start";

    private const string InsertBlockSql = """
        INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at, prev_start)
        VALUES (@start, @first, @last, @months, @hash, @now, @prev)
        ON CONFLICT DO NOTHING
        """;

    // the block in force is the earliest block that is not released; an orphan (a second first block left by the ADR 0207 race, marked by the migration) is never read. With nothing released it is block 1, as the earliest-created row was before.
    public async ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {BlockColumns} FROM lab.holdout_blocks b WHERE NOT b.orphan AND NOT EXISTS (SELECT 1 FROM lab.holdout_releases r WHERE r.block_start = b.start_date) ORDER BY b.start_date ASC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBlock(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredBlock>> ListBlocksAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {BlockColumns}, r.released_at, r.mode, r.actor, r.setting_hash, r.looked, r.next_block_start FROM lab.holdout_blocks b LEFT JOIN lab.holdout_releases r ON r.block_start = b.start_date WHERE NOT b.orphan ORDER BY b.start_date ASC");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredBlock>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var block = ReadBlock(reader);
            rows.Add(new StoredBlock(block, reader.IsDBNull(7)
                ? null
                : new StoredRelease(block.Start, reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetBoolean(11), reader.GetFieldValue<DateOnly>(12))));
        }

        return rows;
    }

    public async ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM lab.holdout_unlocks WHERE block_start = @start AND NOT reused_window LIMIT 1");
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, blockStart);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<bool> InsertBlockAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        await using var command = dataSource.CreateCommand(InsertBlockSql);
        BindBlock(command, block);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<ReleaseOutcome> ReleaseAsync(StoredRelease release, HoldOutBlock next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(next);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO lab.holdout_releases (block_start, released_at, mode, actor, setting_hash, looked, next_block_start)
            VALUES (@block, @at, @mode, @actor, @hash, @looked, @next)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("block", NpgsqlDbType.Date, release.BlockStart);
            insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, release.ReleasedAt.ToUniversalTime());
            insert.Parameters.AddWithValue("mode", NpgsqlDbType.Text, release.Mode);
            insert.Parameters.AddWithValue("actor", NpgsqlDbType.Text, release.Actor);
            insert.Parameters.AddWithValue("hash", NpgsqlDbType.Char, release.SettingHash);
            insert.Parameters.AddWithValue("looked", NpgsqlDbType.Boolean, release.Looked);
            insert.Parameters.AddWithValue("next", NpgsqlDbType.Date, release.NextBlockStart);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ReleaseOutcome.AlreadyReleased;
            }
        }

        await using (var block = new NpgsqlCommand(InsertBlockSql, connection, transaction))
        {
            BindBlock(block, next);
            if (await block.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ReleaseOutcome.ChainConflict;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ReleaseOutcome.Released;
    }

    private static void BindBlock(NpgsqlCommand command, HoldOutBlock block)
    {
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, block.Start);
        command.Parameters.AddWithValue("first", NpgsqlDbType.Date, block.HistoryFirst);
        command.Parameters.AddWithValue("last", NpgsqlDbType.Date, block.HistoryLast);
        command.Parameters.AddWithValue("months", NpgsqlDbType.Integer, block.LengthMonths);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, block.SettingHash);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, block.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("prev", NpgsqlDbType.Date, (object?)block.PrevStart ?? DBNull.Value);
    }

    private static HoldOutBlock ReadBlock(NpgsqlDataReader reader) =>
        new(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt32(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6));

    public async ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} WHERE strategy_id = @id AND version = @v AND kind = @kind AND block_start = @block");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        command.Parameters.AddWithValue("block", NpgsqlDbType.Date, blockStart);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<StoredUnlock?> NewestUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} WHERE strategy_id = @id AND version = @v AND kind = @kind ORDER BY reused_window ASC, unlocked_at DESC, id DESC LIMIT 1");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<bool> HasFreshLookAsync(DateOnly blockStart, string kind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM lab.holdout_unlocks WHERE block_start = @start AND kind = @kind AND NOT reused_window LIMIT 1");
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, blockStart);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async ValueTask<IReadOnlyList<StoredUnlock>> ListFreshLooksAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} WHERE block_start = @start AND NOT reused_window ORDER BY kind");
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, blockStart);
        return await ReadAllAsync(command, cancellationToken);
    }

    public async ValueTask<DateTimeOffset?> FirstLookAtAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT min(unlocked_at) FROM lab.holdout_unlocks WHERE block_start = @start");
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, blockStart);
        return await command.ExecuteScalarAsync(cancellationToken) is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }

    public async ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksByIdPrefixAsync(string kind, string idPrefix, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} WHERE kind = @kind AND starts_with(strategy_id, @prefix) ORDER BY block_start DESC, unlocked_at DESC, id DESC");
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        command.Parameters.AddWithValue("prefix", NpgsqlDbType.Text, idPrefix);
        return await ReadAllAsync(command, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<StoredUnlock>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredUnlock>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<StoredUnlock>> ListUnlocksAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} ORDER BY unlocked_at DESC, id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredUnlock>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<bool> InsertUnlockAsync(StoredUnlock unlock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unlock);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.holdout_unlocks (strategy_id, version, kind, unlocked_at, mode, actor, setting_hash, reused_window, block_start, lab_run_id, result, reason)
            SELECT @id, @v, @kind, @now, @mode, @actor, @hash, @reused, @block, @run, @result, @reason
            WHERE NOT EXISTS (SELECT 1 FROM lab.holdout_releases r WHERE r.block_start = @block)
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, unlock.StrategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, unlock.Version);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, unlock.Kind);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, unlock.UnlockedAt.ToUniversalTime());
        command.Parameters.AddWithValue("mode", NpgsqlDbType.Text, unlock.Mode);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, unlock.Actor);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, unlock.SettingHash);
        command.Parameters.AddWithValue("reused", NpgsqlDbType.Boolean, unlock.ReusedWindow);
        command.Parameters.AddWithValue("block", NpgsqlDbType.Date, unlock.BlockStart);
        command.Parameters.AddWithValue("run", NpgsqlDbType.Uuid, unlock.RunId);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(unlock.Result, Json));
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, (object?)unlock.Reason ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private const string SelectUnlock = "SELECT strategy_id, version, kind, mode, actor, unlocked_at, setting_hash, reused_window, block_start, lab_run_id, result, reason FROM lab.holdout_unlocks";

    private static StoredUnlock Read(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6), reader.GetBoolean(7), reader.GetFieldValue<DateOnly>(8), reader.GetGuid(9), JsonSerializer.Deserialize<HoldOutResultView>(reader.GetString(10), Json)!, reader.IsDBNull(11) ? null : reader.GetString(11));
}
