using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Infrastructure.OpeningBars;

/// <summary>
/// The write side of the opening-minute tables (BK-03b), raw SQL: <c>market.bars_1m_live</c> and <c>market.opening_bar_events</c>, both insert-only for the runtime role. The bars go in with one statement; <c>ON CONFLICT DO NOTHING</c> leaves a minute that is
/// there already (the count that comes back is the new ones). Nothing here reads a tick, a depth table or an account.
/// </summary>
internal sealed class OpeningBarsStore(NpgsqlDataSource dataSource) : IOpeningBarsStore
{
    public async ValueTask<int> AppendAsync(IReadOnlyList<MinuteBar> bars, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            return 0;
        }

        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO market.bars_1m_live (instrument_id, minute_start, o, h, l, c, v, packets)
            SELECT * FROM unnest(@ids, @starts, @o, @h, @l, @c, @v, @packets)
            ON CONFLICT (instrument_id, minute_start) DO NOTHING
            """);
        var p = command.Parameters;
        p.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = bars.Select(b => b.InstrumentId).ToArray() });
        p.Add(new NpgsqlParameter("starts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = bars.Select(b => b.MinuteStart.UtcDateTime).ToArray() });
        p.Add(new NpgsqlParameter("o", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Open).ToArray() });
        p.Add(new NpgsqlParameter("h", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.High).ToArray() });
        p.Add(new NpgsqlParameter("l", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Low).ToArray() });
        p.Add(new NpgsqlParameter("c", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = bars.Select(b => b.Close).ToArray() });
        p.Add(new NpgsqlParameter("v", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = bars.Select(b => b.Volume).ToArray() });
        p.Add(new NpgsqlParameter("packets", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = bars.Select(b => b.Packets).ToArray() });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask AddEventAsync(DateOnly day, OpeningBarsEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        await using var command = dataSource.CreateCommand("INSERT INTO market.opening_bar_events (session_date, kind, from_at, to_at) VALUES (@day, @kind, @from, @to)");
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("kind", @event.Kind);
        command.Parameters.AddWithValue("from", @event.From.UtcDateTime);
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = @event.To is { } to ? to.UtcDateTime : DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return ValueTask.FromResult<long?>(new DriveInfo("/").AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            return ValueTask.FromResult<long?>(null);
        }
    }
}
