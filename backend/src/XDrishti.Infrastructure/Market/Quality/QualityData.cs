using Npgsql;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Infrastructure.Market.Quality;

/// <summary>What the quality checks read besides the 1-minute bars: which instruments to check, and the stored derived bars of one instrument by the primary key (instrument, timeframe, time).</summary>
internal sealed class QualityData(NpgsqlDataSource dataSource) : IQualityData
{
    public async ValueTask<IReadOnlyList<QualityTarget>> ListTargetsAsync(Guid? instrumentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id, i.canonical_symbol, c.first_bar, c.last_bar
            FROM market.bar_coverage c
            JOIN market.instruments i ON i.id = c.instrument_id
            JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
            WHERE i.retired_at IS NULL AND (@instrument IS NULL OR i.id = @instrument)
            ORDER BY i.canonical_symbol
            """);
        command.Parameters.AddWithValue("instrument", NpgsqlTypes.NpgsqlDbType.Uuid, instrumentId is { } id ? id : DBNull.Value);
        var targets = new List<QualityTarget>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(new QualityTarget(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return targets;
    }

    public async ValueTask<IReadOnlyList<StoredBucket>> ReadStoredBucketsAsync(Guid instrumentId, QualityBounds bounds, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT timeframe, extract(epoch FROM ts)::bigint, open, high, low, close, volume, bars
            FROM market.bars_agg
            WHERE instrument_id = @id
              AND ((timeframe = '1W' AND ts >= @week) OR (timeframe = '1M' AND ts >= @month) OR (timeframe NOT IN ('1W', '1M') AND ts >= @window))
            """);
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("window", DateTimeOffset.FromUnixTimeSeconds(bounds.Window));
        command.Parameters.AddWithValue("week", DateTimeOffset.FromUnixTimeSeconds(bounds.Week));
        command.Parameters.AddWithValue("month", DateTimeOffset.FromUnixTimeSeconds(bounds.Month));
        var buckets = new List<StoredBucket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Timeframes.TryParse(reader.GetString(0), out var timeframe))
            {
                buckets.Add(new StoredBucket(timeframe, reader.GetInt64(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetInt64(6), reader.GetInt32(7)));
            }
        }

        return buckets;
    }
}
