using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Infrastructure.Lab;

/// <summary><c>lab.registrations</c> behind <see cref="IRegistrationStore"/> (CF-06, ADR 0207): raw SQL, insert and select only. The five texts are stored as one jsonb document and are never logged.</summary>
internal sealed class RegistrationStore(NpgsqlDataSource dataSource) : IRegistrationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<StoredRegistration?> GetAsync(string strategyId, int version, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT rationale, text_hash, created_at, actor FROM lab.registrations WHERE strategy_id = @id AND version = @v");
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, strategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var registration = JsonSerializer.Deserialize<Registration>(reader.GetString(0), Json)!;
        return new StoredRegistration(strategyId, version, registration, reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3));
    }

    public async ValueTask<bool> InsertAsync(StoredRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO lab.registrations (strategy_id, version, rationale, text_hash, created_at, actor)
            VALUES (@id, @v, @rationale, @hash, @now, @actor)
            ON CONFLICT (strategy_id, version) DO NOTHING
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, registration.StrategyId);
        command.Parameters.AddWithValue("v", NpgsqlDbType.Integer, registration.Version);
        command.Parameters.AddWithValue("rationale", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(registration.Registration, Json));
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, registration.TextHash);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, registration.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, registration.Actor);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
