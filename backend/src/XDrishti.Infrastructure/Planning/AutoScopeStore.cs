using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary><c>planning.auto_scopes</c> behind <see cref="IAutoScopeStore"/> (spec AU-16): insert and select only; the newest row is in force. Raw SQL, no EF entity.</summary>
internal sealed class AutoScopeStore(NpgsqlDataSource dataSource) : IAutoScopeStore
{
    private const string Columns = "id, basket_ids, instrument_ids, scope_hash, created_by, created_at";

    public async ValueTask<StoredAutoScope?> LatestAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM planning.auto_scopes ORDER BY id DESC LIMIT 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<StoredAutoScope> AppendAsync(AutoScope scope, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var command = dataSource.CreateCommand($"INSERT INTO planning.auto_scopes (basket_ids, instrument_ids, scope_hash, created_by, created_at) VALUES (@baskets, @instruments, @hash, @by, @at) RETURNING {Columns}");
        command.Parameters.AddWithValue("baskets", NpgsqlDbType.Array | NpgsqlDbType.Uuid, scope.BasketIds.ToArray());
        command.Parameters.AddWithValue("instruments", NpgsqlDbType.Array | NpgsqlDbType.Uuid, scope.InstrumentIds.ToArray());
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Char, scope.Hash);
        command.Parameters.AddWithValue("by", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static StoredAutoScope Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetFieldValue<Guid[]>(1), r.GetFieldValue<Guid[]>(2), r.GetString(3), r.GetString(4), r.GetFieldValue<DateTimeOffset>(5));
}
