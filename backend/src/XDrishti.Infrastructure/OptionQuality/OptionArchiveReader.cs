using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Infrastructure.OptionQuality;

/// <summary>
/// The read side of the option archive for the quality checks (P14-04, ADR 0178): <c>options.chain_snapshots</c>, <c>chain_requests</c>, <c>chain_sessions</c>, <c>rolling_bars</c>, <c>fetch_log</c> and the pending test against <c>quality_results</c>. Every query is bounded by an India day or a
/// 30-day window and an underlying, or by a limit; it never scans a hypertable without a time range and it writes nothing. The 1-minute closes of an underlying and window are read once and kept for the neighbouring keys.
/// </summary>
internal sealed class OptionArchiveReader(NpgsqlDataSource dataSource) : IOptionArchiveReader
{
    private (Guid Underlying, DateOnly From)? _closesKey;

    private Dictionary<DateTimeOffset, int> _closes = [];

    private (Guid Underlying, DateOnly From)? _activeKey;

    private HashSet<DateOnly> _active = [];

    private static DateTime Start(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).UtcDateTime;

    public async ValueTask<IReadOnlyList<DateOnly>> PendingChainSessionsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken)
    {
        // A day is recorded when a chain request exists for it (a crashed day has requests and an end row of zero, or none). Days are probed one by one from the first request on: each probe is an index range on slot_ts.
        await using var command = dataSource.CreateCommand(
            $"""
            WITH fd AS (SELECT (min(slot_ts) AT TIME ZONE 'Asia/Kolkata')::date AS d FROM options.chain_requests)
            SELECT g.d FROM (SELECT fd.d + n AS d FROM fd, generate_series(0, GREATEST(@upTo::date - fd.d, -1)) n) g
            WHERE {HasRequests("g.d")}
              AND NOT EXISTS (SELECT 1 FROM options.quality_results q WHERE q.source = 'chain' AND q.session_date = g.d)
            ORDER BY g.d LIMIT @limit
            """);
        command.Parameters.AddWithValue("upTo", upTo);
        command.Parameters.AddWithValue("limit", limit);
        var dates = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return dates;
    }

    private static string HasRequests(string day) =>
        $"EXISTS (SELECT 1 FROM options.chain_requests r WHERE r.slot_ts >= ({day}::timestamp AT TIME ZONE 'Asia/Kolkata') AND r.slot_ts < (({day} + 1)::timestamp AT TIME ZONE 'Asia/Kolkata'))";

    public async ValueTask<IReadOnlyList<ChainRequestSummary>> ChainRequestSummariesAsync(DateOnly session, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT underlying_id, outcome, count(*)::int FROM options.chain_requests WHERE slot_ts >= @from AND slot_ts < @to GROUP BY underlying_id, outcome");
        command.Parameters.AddWithValue("from", Start(session));
        command.Parameters.AddWithValue("to", Start(session.AddDays(1)));
        var byUnderlying = new Dictionary<Guid, Dictionary<string, int>>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                if (!byUnderlying.TryGetValue(id, out var outcomes))
                {
                    byUnderlying[id] = outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                outcomes[reader.GetString(1)] = reader.GetInt32(2);
            }
        }

        return [.. byUnderlying.Select(u => new ChainRequestSummary(u.Key, u.Value.Values.Sum(), u.Value.GetValueOrDefault("ok"), u.Value))];
    }

    public async ValueTask<IReadOnlyList<ChainPoint>> ChainPointsAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT ts, expiry, strike_px, side, security_id, underlying_px, bid_px, ask_px, oi, volume, iv, delta, gamma, vega, flags
            FROM options.chain_snapshots
            WHERE underlying_id = @u AND ts >= @from AND ts < @to
            ORDER BY ts, expiry, strike_px, side
            """);
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", Start(session));
        command.Parameters.AddWithValue("to", Start(session.AddDays(1)));
        var points = new List<ChainPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new ChainPoint(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetInt32(2),
                reader.GetString(3) == "CE",
                reader.GetInt64(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                Real(reader, 10),
                Real(reader, 11),
                Real(reader, 12),
                Real(reader, 13),
                reader.GetInt16(14)));
        }

        return points;
    }

    public async ValueTask<IReadOnlySet<DateOnly>> ChainSessionDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await DatesAsync($"SELECT @from::date + n FROM generate_series(0, GREATEST(@to::date - @from::date, -1)) n WHERE {HasRequests("(@from::date + n)")}", from, to, cancellationToken);

    public async ValueTask<IReadOnlySet<DateOnly>> ChainResultDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await DatesAsync("SELECT DISTINCT session_date FROM options.quality_results WHERE source = 'chain' AND session_date BETWEEN @from AND @to", from, to, cancellationToken);

    public async ValueTask<DateOnly?> FirstChainSessionAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT (min(slot_ts) AT TIME ZONE 'Asia/Kolkata')::date FROM options.chain_requests");
        return await command.ExecuteScalarAsync(cancellationToken) switch
        {
            DateTime date => DateOnly.FromDateTime(date),
            DateOnly only => only,
            _ => null,
        };
    }

    public async ValueTask<IReadOnlyList<Guid>> RecentChainUnderlyingsAsync(DateOnly since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT underlying_id FROM options.chain_requests WHERE slot_ts >= @since");
        command.Parameters.AddWithValue("since", Start(since));
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async ValueTask<IReadOnlyList<RollingWindow>> PendingRollingWindowsAsync(DateOnly upTo, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT f.underlying_id, f.expiry_flag, f.expiry_code, f.atm_offset, f.side, f.interval_minutes, f.window_from, f.window_to, f.outcome, f.fetch_key
            FROM (SELECT l.*, format('%s:%s%s:%s:%s:%s', l.underlying_id, l.expiry_flag, l.expiry_code, l.atm_offset, l.side, l.interval_minutes) AS fetch_key
                  FROM options.fetch_log l WHERE l.outcome IN ('ok', 'empty', 'no_data') AND l.window_to <= @upTo) f
            WHERE NOT EXISTS (SELECT 1 FROM options.quality_results q WHERE q.fetch_key = f.fetch_key AND q.session_date >= f.window_from AND q.session_date < f.window_to)
            ORDER BY f.window_from, f.underlying_id, f.id
            LIMIT @limit
            """);
        command.Parameters.AddWithValue("upTo", upTo);
        command.Parameters.AddWithValue("limit", limit);
        var windows = new List<RollingWindow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            windows.Add(new RollingWindow(reader.GetGuid(0), reader.GetString(1)[0], reader.GetInt16(2), reader.GetInt16(3), reader.GetString(4), reader.GetInt16(5), reader.GetFieldValue<DateOnly>(6), reader.GetFieldValue<DateOnly>(7), reader.GetString(8), reader.GetString(9)));
        }

        return windows;
    }

    public async ValueTask<IReadOnlyList<RollingPoint>> RollingPointsAsync(RollingWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        var closes = await ClosesAsync(window.UnderlyingId, window.From, window.To, cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            SELECT ts, strike_px, spot_px FROM options.rolling_bars
            WHERE underlying_id = @u AND expiry_flag = @flag AND expiry_code = @code AND atm_offset = @offset AND side = @side AND interval_minutes = @interval AND ts >= @from AND ts < @to
            ORDER BY ts
            """);
        command.Parameters.AddWithValue("u", window.UnderlyingId);
        command.Parameters.Add(new NpgsqlParameter("flag", NpgsqlDbType.Char) { Value = window.ExpiryFlag.ToString() });
        command.Parameters.AddWithValue("code", (short)window.ExpiryCode);
        command.Parameters.AddWithValue("offset", (short)window.Offset);
        command.Parameters.Add(new NpgsqlParameter("side", NpgsqlDbType.Char) { Value = window.Side });
        command.Parameters.AddWithValue("interval", (short)window.IntervalMinutes);
        command.Parameters.AddWithValue("from", Start(window.From));
        command.Parameters.AddWithValue("to", Start(window.To));
        var points = new List<RollingPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ts = reader.GetFieldValue<DateTimeOffset>(0);
            var end = ts.AddMinutes(window.IntervalMinutes - 1).ToUniversalTime();
            points.Add(new RollingPoint(ts, reader.GetInt32(1), reader.GetInt32(2), closes.TryGetValue(end, out var close) ? close : null));
        }

        return points;
    }

    public async ValueTask<IReadOnlySet<DateOnly>> ActiveDaysAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (_activeKey == (underlyingId, from))
        {
            return _active;
        }

        await using var command = dataSource.CreateCommand("SELECT DISTINCT (ts AT TIME ZONE 'Asia/Kolkata')::date FROM options.rolling_bars WHERE underlying_id = @u AND ts >= @from AND ts < @to");
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", Start(from));
        command.Parameters.AddWithValue("to", Start(to));
        var days = new HashSet<DateOnly>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add(reader.GetFieldValue<DateOnly>(0));
            }
        }

        _activeKey = (underlyingId, from);
        _active = days;
        return days;
    }

    private async ValueTask<Dictionary<DateTimeOffset, int>> ClosesAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (_closesKey == (underlyingId, from))
        {
            return _closes;
        }

        await using var command = dataSource.CreateCommand("SELECT ts, round(close * 100)::int FROM market.bars_1m WHERE instrument_id = @u AND ts >= @from AND ts < @to");
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("from", Start(from));
        command.Parameters.AddWithValue("to", Start(to));
        var closes = new Dictionary<DateTimeOffset, int>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                closes[reader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime()] = reader.GetInt32(1);
            }
        }

        _closesKey = (underlyingId, from);
        _closes = closes;
        return closes;
    }

    private async ValueTask<IReadOnlySet<DateOnly>> DatesAsync(string sql, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var dates = new HashSet<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return dates;
    }

    private static double? Real(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFloat(ordinal);
}
