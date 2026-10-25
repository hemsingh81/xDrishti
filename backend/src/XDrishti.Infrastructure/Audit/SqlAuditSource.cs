using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Audit;

/// <summary>
/// The shared read of an insert-only store (spec P8-22): raw SQL with named columns (never <c>*</c>), SELECT only, bounded by the window and <c>Take</c>, keyset by <c>(time, id)</c> with a row comparison so the index of the time column serves it.
/// A store names its table, time column, kind expression and eight list columns (id, time, kind, actor, account id, plan id, item id, text); the detail read adds the columns that only the detail shows.
/// </summary>
internal abstract class SqlAuditSource(NpgsqlDataSource dataSource) : IAuditSource
{
    public abstract AuditSource Source { get; }

    protected abstract string Table { get; }

    protected abstract string TimeColumn { get; }

    /// <summary>A SQL expression for the stored kind of a row.</summary>
    protected abstract string KindExpression { get; }

    /// <summary>The eight list columns, in the order id, time, kind, actor, account id, plan id, item id, text.</summary>
    protected abstract string ListColumns { get; }

    /// <summary>Extra columns of the detail read (after the eight), or empty.</summary>
    protected virtual string DetailColumns => string.Empty;

    protected virtual bool HasAccount => false;

    protected virtual IReadOnlyList<AuditField> Fields(NpgsqlDataReader reader) => [];

    public async ValueTask<IReadOnlyList<AuditRaw>> ReadAsync(AuditRead read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        var sql = new StringBuilder($"SELECT {ListColumns} FROM {Table} WHERE {TimeColumn} >= @from AND {TimeColumn} < @to");
        await using var command = dataSource.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = read.From.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = read.To.ToUniversalTime() });
        if (read.After is { } after)
        {
            command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.TimestampTz) { Value = after.At.ToUniversalTime() });
            if (!after.SameAt)
            {
                sql.Append($" AND {TimeColumn} < @after");
            }
            else if (after.BeforeId is null)
            {
                sql.Append($" AND {TimeColumn} <= @after");
            }
            else
            {
                sql.Append($" AND ({TimeColumn}, id) < (@after, @afterId)");
                command.Parameters.Add(IdParameter("afterId", after.BeforeId));
            }
        }

        if (read.Kinds is not null)
        {
            sql.Append($" AND {KindExpression} = ANY(@kinds)");
            command.Parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = read.Kinds.ToArray() });
        }

        if (read.AccountId is { } account && HasAccount)
        {
            sql.Append(" AND account_id = @account");
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = account });
        }

        sql.Append($" ORDER BY {TimeColumn} DESC, id DESC LIMIT @take");
        command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = Math.Clamp(read.Take, 1, 600) });
        command.CommandText = sql.ToString();

        var rows = new List<AuditRaw>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, withFields: false));
        }

        return rows;
    }

    public async ValueTask<AuditRaw?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {ListColumns}{DetailColumns} FROM {Table} WHERE id = @id");
        command.Parameters.Add(IdParameter("id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, withFields: true) : null;
    }

    public async ValueTask<IReadOnlyList<AuditKindCount>> CountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {KindExpression}, count(*) FROM {Table} WHERE {TimeColumn} >= @from AND {TimeColumn} < @to GROUP BY 1");
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.ToUniversalTime() });
        var counts = new List<AuditKindCount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new AuditKindCount(reader.GetString(0), checked((int)reader.GetInt64(1))));
        }

        return counts;
    }

    protected static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    protected static string IstText(DateTimeOffset instant) => IndiaTime.ToIst(instant).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " IST";

    private NpgsqlParameter IdParameter(string name, string id) =>
        AuditSources.IsUuid(Source)
            ? new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = Guid.ParseExact(id, "D") }
            : new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = long.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture) };

    private AuditRaw Map(NpgsqlDataReader reader, bool withFields) => new(
        Source,
        AuditSources.IsUuid(Source) ? reader.GetFieldValue<Guid>(0).ToString("D") : reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
        reader.GetFieldValue<DateTimeOffset>(1),
        reader.GetString(2),
        Text(reader, 3),
        reader.IsDBNull(4) ? null : reader.GetFieldValue<Guid>(4),
        Text(reader, 7),
        reader.IsDBNull(5) ? null : reader.GetInt64(5),
        reader.IsDBNull(6) ? null : reader.GetInt64(6),
        withFields ? Fields(reader) : []);
}
