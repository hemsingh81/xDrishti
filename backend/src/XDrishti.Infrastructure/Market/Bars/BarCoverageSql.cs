namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>SQL shared by everything that writes bars: keeping <c>market.bar_coverage</c> true to <c>market.bars_1m</c>.</summary>
internal static class BarCoverageSql
{
    /// <summary>
    /// Recomputes the coverage of one instrument from its stored bars and widens its changed range to [@dirtyFrom, @dirtyTo] (both NULL: no change). Parameters:
    /// @id, @now, @dirtyFrom, @dirtyTo. An instrument without bars gets no row.
    /// </summary>
    public const string Refresh =
        """
        INSERT INTO market.bar_coverage AS c
            (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at, dirty_from, dirty_to, aggregates)
        SELECT @id, min(ts), max(ts), count(*), count(*) FILTER (WHERE source = 'csv'), count(*) FILTER (WHERE source = 'dhan'), @now,
               @dirtyFrom, @dirtyTo, '{}'::jsonb
        FROM market.bars_1m
        WHERE instrument_id = @id
        HAVING count(*) > 0
        ON CONFLICT (instrument_id) DO UPDATE SET
            first_bar = EXCLUDED.first_bar, last_bar = EXCLUDED.last_bar, bars = EXCLUDED.bars,
            csv_bars = EXCLUDED.csv_bars, dhan_bars = EXCLUDED.dhan_bars, updated_at = EXCLUDED.updated_at,
            dirty_from = LEAST(c.dirty_from, EXCLUDED.dirty_from), dirty_to = GREATEST(c.dirty_to, EXCLUDED.dirty_to)
        """;
}
