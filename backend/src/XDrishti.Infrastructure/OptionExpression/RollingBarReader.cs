using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary>
/// The re-mark's bounded read of <c>options.rolling_bars</c> and <c>options.fetch_log</c> (P14-08, ADR 0197): the bars of one series (underlying, kind, code, side, width) over one time range, all offsets, selected by the whole leading primary key; and the highest finished
/// fetch-log id of the underlying over a date range. A range longer than <see cref="IRollingBarReader.MaxRangeDays"/> is refused before any query. It writes nothing; the runtime role can only read the tables anyway.
/// </summary>
internal sealed class RollingBarReader(NpgsqlDataSource dataSource) : IRollingBarReader
{
    public async ValueTask<IReadOnlyList<OptionBar>> BarsAsync(Guid underlyingId, char expiryFlag, int expiryCode, string side, int intervalMinutes, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(side);
        if (to <= from || (to - from).TotalDays > IRollingBarReader.MaxRangeDays)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "A rolling-bar read covers a range of at least a moment and at most " + IRollingBarReader.MaxRangeDays + " days.");
        }

        await using var command = dataSource.CreateCommand(
            """
            SELECT ts, atm_offset, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, oi, iv, flags
            FROM options.rolling_bars
            WHERE underlying_id = @u AND expiry_flag = @flag AND expiry_code = @code AND side = @side AND interval_minutes = @interval AND ts >= @from AND ts < @to
            ORDER BY ts, atm_offset
            """);
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.Add(new NpgsqlParameter("flag", NpgsqlDbType.Char) { Value = expiryFlag.ToString() });
        command.Parameters.AddWithValue("code", (short)expiryCode);
        command.Parameters.Add(new NpgsqlParameter("side", NpgsqlDbType.Char) { Value = side });
        command.Parameters.AddWithValue("interval", (short)intervalMinutes);
        command.Parameters.AddWithValue("from", from.UtcDateTime);
        command.Parameters.AddWithValue("to", to.UtcDateTime);
        var bars = new List<OptionBar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(new OptionBar(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetInt16(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetFloat(10),
                (reader.GetInt16(11) & 1) != 0,
                false));
        }

        return bars;
    }

    public async ValueTask<long> WatermarkAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT coalesce(max(id), 0) FROM options.fetch_log WHERE underlying_id = @u AND outcome IN ('ok', 'empty', 'no_data') AND window_from <= @to AND window_to > @from");
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
