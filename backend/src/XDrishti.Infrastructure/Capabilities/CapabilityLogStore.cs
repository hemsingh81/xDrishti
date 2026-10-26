using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Capabilities;

/// <summary>
/// The capability log behind its two ports (spec P8-18 Part A, ADR 0128): <c>platform.capability_changes</c>, raw SQL, select and insert only. A switch is a new row; the newest id of a name decides and a name with no row is OFF. The append takes the lock the insert trigger takes,
/// re-reads the newest row of the name and writes nothing when it already says the same (two switches at once make one row). The database stamps the id, the time, the previous id and the snapshot hash.
/// </summary>
internal sealed class CapabilityLogStore(NpgsqlDataSource dataSource) : ICapabilityLog, ICapabilityLogWriter
{
    private const string Columns = "id, capability, enabled, reason, actor, changed_at";

    public async ValueTask<IReadOnlyList<CapabilityEntry>> NewestAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var select = new NpgsqlCommand($"SELECT DISTINCT ON (capability) {Columns} FROM platform.capability_changes ORDER BY capability, id DESC", connection);
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<CapabilityEntry>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var select = new NpgsqlCommand($"SELECT {Columns} FROM platform.capability_changes ORDER BY id DESC LIMIT @limit", connection);
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<Result<CapabilityAppend>> AppendAsync(CapabilityChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The same lock the insert trigger takes, taken first so the read below and the insert cannot be interleaved with another switch.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('platform.capability_changes', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        CapabilityEntry? newest = null;
        await using (var read = new NpgsqlCommand($"SELECT {Columns} FROM platform.capability_changes WHERE capability = @name ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            read.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, change.Name);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                newest = Read(reader);
            }
        }

        var on = newest?.Enabled ?? false; // no row means off
        if (on == change.Enabled)
        {
            return Result.Success(new CapabilityAppend(newest, Created: false)); // already so
        }

        await using var insert = new NpgsqlCommand($"INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES (@name, @enabled, @reason, @actor) RETURNING {Columns}", connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("name", NpgsqlDbType.Varchar, change.Name);
        p.AddWithValue("enabled", NpgsqlDbType.Boolean, change.Enabled);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, change.Reason);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, change.Actor);
        CapabilityEntry row;
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            row = Read(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success(new CapabilityAppend(row, Created: true));
    }

    private static async Task<IReadOnlyList<CapabilityEntry>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<CapabilityEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static CapabilityEntry Read(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
}
