using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// The dry run of a CSV merge (ADR 0017 §6): the same classification as <see cref="BarStore"/> merges with — new, changed, same, and a stored Dhan bar kept — computed by joining the given bars to the
/// stored ones of the instrument on the primary key (instrument, bar start), read-only. The bars travel as arrays, so nothing is created or written.
/// </summary>
internal sealed class BarComparer(NpgsqlDataSource dataSource) : IBarComparison
{
    private const int TimeoutSeconds = 300;

    // Mirrors BarStore.Classify for the source 'csv': a missing minute is new, a Dhan bar is kept, a CSV bar with other values is changed, everything else is the same.
    private const string Classify =
        """
        WITH s AS (
            SELECT * FROM unnest(@ts, @open, @high, @low, @close, @volume) AS t(ts, open, high, low, close, volume)),
        j AS (
            SELECT CASE
                WHEN b.ts IS NULL THEN 'new'
                WHEN b.source = 'dhan' THEN 'kept'
                WHEN (b.open, b.high, b.low, b.close, b.volume) IS DISTINCT FROM (s.open, s.high, s.low, s.close, s.volume) THEN 'changed'
                ELSE 'same'
            END AS kind
            FROM s
            LEFT JOIN market.bars_1m b ON b.instrument_id = @id AND b.ts = s.ts)
        SELECT count(*) FILTER (WHERE kind = 'new'), count(*) FILTER (WHERE kind = 'changed'), count(*) FILTER (WHERE kind = 'same'), count(*) FILTER (WHERE kind = 'kept')
        FROM j
        """;

    public async ValueTask<BarComparisonResult> CompareAsync(Guid instrumentId, ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken)
    {
        if (bars.IsEmpty)
        {
            return new BarComparisonResult(0, 0, 0, 0);
        }

        var span = bars.Span;
        var ts = new DateTime[span.Length];
        var open = new double[span.Length];
        var high = new double[span.Length];
        var low = new double[span.Length];
        var close = new double[span.Length];
        var volume = new long[span.Length];
        for (var i = 0; i < span.Length; i++)
        {
            ts[i] = DateTime.UnixEpoch.AddSeconds(span[i].EpochSeconds);
            open[i] = span[i].Open;
            high[i] = span[i].High;
            low[i] = span[i].Low;
            close[i] = span[i].Close;
            volume[i] = span[i].Volume;
        }

        await using var command = dataSource.CreateCommand(Classify);
        command.CommandTimeout = TimeoutSeconds;
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("ts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, ts);
        command.Parameters.AddWithValue("open", NpgsqlDbType.Array | NpgsqlDbType.Double, open);
        command.Parameters.AddWithValue("high", NpgsqlDbType.Array | NpgsqlDbType.Double, high);
        command.Parameters.AddWithValue("low", NpgsqlDbType.Array | NpgsqlDbType.Double, low);
        command.Parameters.AddWithValue("close", NpgsqlDbType.Array | NpgsqlDbType.Double, close);
        command.Parameters.AddWithValue("volume", NpgsqlDbType.Array | NpgsqlDbType.Bigint, volume);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new BarComparisonResult(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }
}
