using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.holdout_blocks</c> and <c>lab.holdout_unlocks</c> behind <see cref="IHoldOutStore"/> (CF-06, ADR 0207): raw SQL, insert and select only. No market data is read here.</summary>
internal sealed class HoldOutStore(NpgsqlDataSource dataSource) : IHoldOutStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // shortcut: two concurrent first posts can each store a block with a different start (the unique index is on the start). The earliest stored block is the block (ORDER BY created_at ASC), so every reader sees the same one; the later row is never read. A single-row index is the follow-up if blocks are ever re-cut.
    public async ValueTask<HoldOutBlock?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT start_date, history_first, history_last, length_months, setting_hash, created_at FROM lab.holdout_blocks ORDER BY created_at ASC, id ASC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new HoldOutBlock(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt32(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5))
            : null;
    }

    public async ValueTask<bool> InsertBlockAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at)
            VALUES (@start, @first, @last, @months, @hash, @now)
            ON CONFLICT (start_date) DO NOTHING
            """);
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, block.Start);
        command.Parameters.AddWithValue("first", NpgsqlDbType.Date, block.HistoryFirst);
        command.Parameters.AddWithValue("last", NpgsqlDbType.Date, block.HistoryLast);
        command.Parameters.AddWithValue("months", NpgsqlDbType.Integer, block.LengthMonths);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, block.SettingHash);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, block.CreatedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<StoredUnlock?> GetUnlockAsync(string strategyId, int version, string kind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"{SelectUnlock} WHERE strategy_id = @id AND version = @v AND kind = @kind");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Text, kind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<bool> AnyUnlockAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM lab.holdout_unlocks WHERE block_start = @start LIMIT 1");
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, blockStart);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
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
            INSERT INTO lab.holdout_unlocks (strategy_id, version, kind, unlocked_at, mode, actor, setting_hash, reused_window, block_start, lab_run_id, result)
            VALUES (@id, @v, @kind, @now, @mode, @actor, @hash, @reused, @block, @run, @result)
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
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private const string SelectUnlock = "SELECT strategy_id, version, kind, mode, actor, unlocked_at, setting_hash, reused_window, block_start, lab_run_id, result FROM lab.holdout_unlocks";

    private static StoredUnlock Read(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6), reader.GetBoolean(7), reader.GetFieldValue<DateOnly>(8), reader.GetGuid(9), JsonSerializer.Deserialize<HoldOutResultView>(reader.GetString(10), Json)!);
}
