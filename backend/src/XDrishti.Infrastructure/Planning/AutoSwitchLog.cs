using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.AutoSwitch;

namespace XDrishti.Infrastructure.Planning;

/// <summary><c>planning.auto_switch_changes</c> behind <see cref="IAutoSwitchLog"/> (spec AU-17): insert and select only. Raw SQL, no EF entity.</summary>
internal sealed class AutoSwitchLog(NpgsqlDataSource dataSource) : IAutoSwitchLog
{
    private const string Columns = "id, kind, actor, at, changed, snapshot::text";

    public async ValueTask<IReadOnlyList<AutoSwitchChange>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM planning.auto_switch_changes ORDER BY id DESC LIMIT @limit");
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<AutoSwitchChange>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<AutoSwitchChange> AppendAsync(string kind, string actor, DateTimeOffset at, IReadOnlyList<string> changed, IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var command = dataSource.CreateCommand($"INSERT INTO planning.auto_switch_changes (kind, actor, at, changed, snapshot) VALUES (@kind, @actor, @at, @changed, @snapshot) RETURNING {Columns}");
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, kind);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, at);
        command.Parameters.AddWithValue("changed", NpgsqlDbType.Array | NpgsqlDbType.Varchar, changed.ToArray());
        command.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new SortedDictionary<string, string>(new Dictionary<string, string>(snapshot), StringComparer.Ordinal)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Read(reader);
    }

    private static AutoSwitchChange Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<string[]>(4), JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(5)) ?? []);
}
