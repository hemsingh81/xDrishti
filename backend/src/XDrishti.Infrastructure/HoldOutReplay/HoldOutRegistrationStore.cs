using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Replay;

namespace XDrishti.Infrastructure.HoldOutReplay;

/// <summary>
/// <c>research.replay_holdout_registrations</c>, <c>research.replay_holdout_results</c> and <c>research.replay_holdout_settings</c> (SR-03) behind <see cref="IHoldOutRegistrationStore"/> and <see cref="IHoldOutAutoSettings"/>: raw SQL, insert and select only, all insert-only for the
/// runtime role. The only code that names these tables (an architecture test pins it): the results are research only and no evidence reader may read them. No market data is read here.
/// </summary>
internal sealed class HoldOutRegistrationStore(NpgsqlDataSource dataSource) : IHoldOutRegistrationStore, IHoldOutAutoSettings
{
    private const string Columns = "id, block_start, config::text, config_hash, mode, actor, created_at";

    public async ValueTask InsertAsync(StoredHoldOutRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await using var command = dataSource.CreateCommand("INSERT INTO research.replay_holdout_registrations (id, block_start, config, config_hash, mode, actor, created_at) VALUES (@id, @block, @config, @hash, @mode, @actor, @at)");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, registration.Id);
        command.Parameters.AddWithValue("block", NpgsqlDbType.Date, registration.BlockStart);
        command.Parameters.AddWithValue("config", NpgsqlDbType.Jsonb, registration.ConfigJson);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, registration.ConfigHash);
        command.Parameters.AddWithValue("mode", NpgsqlDbType.Text, registration.Mode);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, registration.Actor);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, registration.CreatedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<StoredHoldOutRegistration?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.replay_holdout_registrations WHERE id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
        var rows = await ReadAsync(command, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.replay_holdout_registrations ORDER BY created_at DESC, id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ForBlockAsync(DateOnly blockStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM research.replay_holdout_registrations WHERE block_start = @block ORDER BY created_at DESC, id DESC");
        command.Parameters.AddWithValue("block", NpgsqlDbType.Date, blockStart);
        return await ReadAsync(command, cancellationToken);
    }

    public async ValueTask<bool> InsertResultAsync(StoredHoldOutResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO research.replay_holdout_results (registration_id, unlock_id, run_ids, verdict, figures, engine_versions, mode, actor, at)
            VALUES (@id, @unlock, @runs, @verdict, @figures, @engines, @mode, @actor, @at)
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, result.RegistrationId);
        command.Parameters.AddWithValue("unlock", NpgsqlDbType.Uuid, result.UnlockId);
        command.Parameters.AddWithValue("runs", NpgsqlDbType.Array | NpgsqlDbType.Uuid, result.RunIds.ToArray());
        command.Parameters.AddWithValue("verdict", NpgsqlDbType.Text, result.Verdict);
        command.Parameters.AddWithValue("figures", NpgsqlDbType.Jsonb, result.FiguresJson);
        command.Parameters.AddWithValue("engines", NpgsqlDbType.Text, result.EngineVersions);
        command.Parameters.AddWithValue("mode", NpgsqlDbType.Text, result.Mode);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, result.Actor);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, result.At.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask<StoredHoldOutResult?> GetResultAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT registration_id, unlock_id, run_ids, verdict, figures::text, engine_versions, mode, actor, at FROM research.replay_holdout_results WHERE registration_id = @id");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, registrationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredHoldOutResult(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<Guid[]>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8))
            : null;
    }

    public async ValueTask<HoldOutAutoSwitches> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT DISTINCT ON (name) name, enabled FROM research.replay_holdout_settings ORDER BY name, at DESC, id DESC");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            bool register = false, run = false; // a name with no row reads OFF
            while (await reader.ReadAsync(cancellationToken))
            {
                var enabled = reader.GetBoolean(1);
                switch (reader.GetString(0))
                {
                    case "replay.holdout.auto_register":
                        register = enabled;
                        break;
                    case "replay.holdout.auto_run":
                        run = enabled;
                        break;
                }
            }

            return new HoldOutAutoSwitches(register, run);
        }
        catch (NpgsqlException)
        {
            return new HoldOutAutoSwitches(null, null); // unreadable reads unavailable and nothing acts
        }
    }

    public async ValueTask AppendAsync(string name, bool enabled, string actor, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO research.replay_holdout_settings (name, enabled, actor, reason, at) VALUES (@name, @enabled, @actor, @reason, @at)");
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, name);
        command.Parameters.AddWithValue("enabled", NpgsqlDbType.Boolean, enabled);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, actor);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<StoredHoldOutRegistration>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredHoldOutRegistration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StoredHoldOutRegistration(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return rows;
    }
}
