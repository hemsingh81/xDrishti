using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>The halts of the live accounts (<c>trading.live_halts</c>, spec P11-04, ADR 0151): raw SQL, insert and select only. One opened row per account, kind and period; a second insert is ignored, so the evaluation is idempotent.</summary>
internal sealed class LiveHaltStore(NpgsqlDataSource dataSource) : ILiveHaltStore
{
    private const string Columns = "id, account_id, kind, event, period_start, limit_r, limit_pct, measured_r, measured_pct, measure_state, action, created_at";

    public async ValueTask<StoredHalt?> OpenAsync(NewHalt halt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(halt);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO trading.live_halts (account_id, kind, event, period_start, limit_r, limit_pct, measured_r, measured_pct, measure_state, action)
            VALUES (@a, @kind, 'opened', @period, @lr, @lp, @mr, @mp, @state, @action)
            ON CONFLICT (account_id, kind, period_start, event) DO NOTHING RETURNING {Columns}
            """);
        command.Parameters.AddWithValue("a", NpgsqlDbType.Uuid, halt.AccountId);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, halt.Kind);
        command.Parameters.AddWithValue("period", NpgsqlDbType.Date, halt.PeriodStart);
        command.Parameters.AddWithValue("lr", NpgsqlDbType.Numeric, halt.LimitR);
        command.Parameters.AddWithValue("lp", NpgsqlDbType.Numeric, halt.LimitPct);
        command.Parameters.AddWithValue("mr", NpgsqlDbType.Double, halt.MeasuredR);
        command.Parameters.Add(new NpgsqlParameter("mp", NpgsqlDbType.Double) { Value = (object?)halt.MeasuredPct ?? DBNull.Value });
        command.Parameters.AddWithValue("state", NpgsqlDbType.Varchar, halt.MeasureState);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Varchar, halt.Action);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<StoredHalt>> ListAsync(Guid? accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM trading.live_halts WHERE (@a IS NULL OR account_id = @a) AND period_start BETWEEN @from AND @to ORDER BY id DESC");
        command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = (object?)accountId ?? DBNull.Value });
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        var rows = new List<StoredHalt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static StoredHalt Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetGuid(1), r.GetString(2), r.GetString(3), DateOnly.FromDateTime(r.GetDateTime(4)), r.GetDecimal(5), r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetDouble(7), r.IsDBNull(8) ? null : r.GetDouble(8), r.GetString(9), r.GetString(10), r.GetFieldValue<DateTimeOffset>(11));
}
