using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// The stored market facts of the execution feedback (spec P11-02), read with index lookups on the instrument master, the daily aggregates and the 1-minute bars. Reads only; nothing here calls a broker. The symbol is the plain ticker of the live rows, looked up as the canonical symbol <c>NSE:ticker</c>.
/// </summary>
internal sealed class LiveExecutionMarket(NpgsqlDataSource dataSource) : ILiveExecutionMarket
{
    private const int TurnoverSessions = 20;
    private const decimal CroreRupees = 10_000_000m;

    /// <summary>The live rows carry the plain ticker (<c>RELIANCE</c>); the instrument master's canonical symbol has the exchange in front (<c>NSE:RELIANCE</c>).</summary>
    private static string Canonical(string symbol) => "NSE:" + symbol;

    public async ValueTask<decimal?> TickAsync(string symbol, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT tick_size FROM market.instruments WHERE canonical_symbol = @symbol");
        command.Parameters.AddWithValue("symbol", Canonical(symbol));
        var paise = await command.ExecuteScalarAsync(cancellationToken);
        var spec = TickSpec.FromMasterPaise(paise is decimal p ? p : null);
        return spec.IsSuccess && !spec.Value.FromFallback ? spec.Value.Size : null;
    }

    public async ValueTask<decimal?> AdvTurnoverCrAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT count(*), avg(close * volume)
            FROM (SELECT a.close, a.volume FROM market.bars_agg a JOIN market.instruments i ON i.id = a.instrument_id
                  WHERE i.canonical_symbol = @symbol AND a.timeframe = '1D' AND a.ts < @before ORDER BY a.ts DESC LIMIT @sessions) d
            """);
        command.Parameters.AddWithValue("symbol", Canonical(symbol));
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.TimestampTz) { Value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).UtcDateTime });
        command.Parameters.AddWithValue("sessions", TurnoverSessions);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetInt64(0) < TurnoverSessions || reader.IsDBNull(1))
        {
            return null;
        }

        var rupees = Convert.ToDecimal(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
        return rupees > 0 ? rupees / CroreRupees : null;
    }

    public async ValueTask<decimal?> MinuteOpenAsync(string symbol, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT b.open FROM market.bars_1m b JOIN market.instruments i ON i.id = b.instrument_id
            WHERE i.canonical_symbol = @symbol AND b.ts = date_trunc('minute', @at)
            """);
        command.Parameters.AddWithValue("symbol", Canonical(symbol));
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = at.UtcDateTime });
        var open = await command.ExecuteScalarAsync(cancellationToken);
        return open is null or DBNull ? null : Convert.ToDecimal(open, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask<(decimal? Low, decimal? High)> MinuteExtremesAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT min(b.low), max(b.high) FROM market.bars_1m b JOIN market.instruments i ON i.id = b.instrument_id
            WHERE i.canonical_symbol = @symbol AND b.ts >= date_trunc('minute', @from) AND b.ts <= date_trunc('minute', @to)
            """);
        command.Parameters.AddWithValue("symbol", Canonical(symbol));
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.UtcDateTime });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (null, null);
        }

        return (reader.IsDBNull(0) ? null : Convert.ToDecimal(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture), reader.IsDBNull(1) ? null : Convert.ToDecimal(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture));
    }
}
