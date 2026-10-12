using Npgsql;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>Reads of the stored bars and their coverage. Totals come from the small coverage table (one row per instrument), never from a scan of the bars.</summary>
internal sealed class BarQueries(NpgsqlDataSource dataSource) : IBarQueries
{
    public async ValueTask<BarTotals> GetBarTotalsAsync(DateTimeOffset expectedStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT count(*), COALESCE(sum(bars), 0)::bigint, min(first_bar), max(last_bar), count(*) FILTER (WHERE last_bar < @expectedStart),
                   (SELECT count(*) FROM market.tracked_instruments t WHERE t.active AND NOT EXISTS (SELECT 1 FROM market.bar_coverage x WHERE x.instrument_id = t.instrument_id))
            FROM market.bar_coverage
            """);
        command.Parameters.AddWithValue("expectedStart", expectedStart.ToUniversalTime());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new BarTotals(
            (int)reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(2)),
            reader.IsDBNull(3) ? null : IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(3)),
            (int)reader.GetInt64(4),
            (int)reader.GetInt64(5));
    }

    public async ValueTask<AggregateTotals> GetAggregateTotalsAsync(CancellationToken cancellationToken)
    {
        var stats = new List<TimeframeStat>();
        await using (var perTimeframe = dataSource.CreateCommand(
            """
            SELECT e.key, sum((e.value ->> 'bars')::bigint)::bigint, max((e.value ->> 'last')::bigint)
            FROM market.bar_coverage c, jsonb_each(c.aggregates) e
            GROUP BY e.key
            """))
        await using (var reader = await perTimeframe.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stats.Add(new TimeframeStat(reader.GetString(0), reader.GetInt64(1), IndiaTime.ToIst(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)))));
            }
        }

        await using var overall = dataSource.CreateCommand("SELECT max(aggregates_built_at), count(*) FILTER (WHERE dirty_from IS NOT NULL) FROM market.bar_coverage");
        await using var overallReader = await overall.ExecuteReaderAsync(cancellationToken);
        await overallReader.ReadAsync(cancellationToken);
        return new AggregateTotals(stats, overallReader.IsDBNull(0) ? null : overallReader.GetFieldValue<DateTimeOffset>(0), (int)overallReader.GetInt64(1));
    }

    public async ValueTask<DataInstrumentPage> ListInstrumentsAsync(DataInstrumentSearch search, DateTimeOffset expectedStart, DateOnly expectedSession, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(calendar);
        const string From =
            """
            FROM market.instruments i
            LEFT JOIN market.bar_coverage c ON c.instrument_id = i.id
            LEFT JOIN market.tracked_instruments t ON t.instrument_id = i.id
            LEFT JOIN (SELECT instrument_id, count(*)::int AS open_gaps FROM market.bar_gaps WHERE status = 'open' GROUP BY instrument_id) g
                   ON g.instrument_id = i.id AND t.active IS TRUE AND i.retired_at IS NULL
            WHERE (c.instrument_id IS NOT NULL OR t.active IS TRUE)
              AND (NOT @hasGaps OR g.open_gaps > 0)
              AND (@pattern IS NULL OR i.canonical_symbol ILIKE @pattern ESCAPE '\' OR i.name ILIKE @pattern ESCAPE '\' OR i.display_name ILIKE @pattern ESCAPE '\')
              AND (@status IS NULL
                   OR (@status = 'empty' AND c.instrument_id IS NULL)
                   OR (@status = 'current' AND c.last_bar >= @expectedStart)
                   OR (@status = 'behind' AND c.last_bar < @expectedStart))
            """;

        var order = search.SortField switch
        {
            DataSortField.Name => "i.name",
            DataSortField.FirstBar => "c.first_bar",
            DataSortField.LastBar => "c.last_bar",
            DataSortField.Bars => "COALESCE(c.bars, 0)",
            _ => "i.canonical_symbol",
        };
        var direction = search.Descending ? "DESC" : "ASC";

        void Bind(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("pattern", NpgsqlTypes.NpgsqlDbType.Text, search.Text is null ? DBNull.Value : $"%{LikePattern.Escape(search.Text)}%");
            command.Parameters.AddWithValue("status", NpgsqlTypes.NpgsqlDbType.Text, search.Status is { } s ? s.ToString().ToLowerInvariant() : DBNull.Value);
            command.Parameters.AddWithValue("expectedStart", expectedStart.ToUniversalTime());
            command.Parameters.AddWithValue("hasGaps", search.HasGaps);
        }

        int total;
        await using (var count = dataSource.CreateCommand($"SELECT count(*) {From}"))
        {
            Bind(count);
            total = (int)(long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

#pragma warning disable CA2100 // the ORDER BY comes from two fixed switches above, the values are parameters
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT i.id, i.canonical_symbol, i.name, i.type, t.active IS TRUE, c.first_bar, c.last_bar, COALESCE(c.bars, 0), COALESCE(c.csv_bars, 0), COALESCE(c.dhan_bars, 0), COALESCE(g.open_gaps, 0)
            {From}
            ORDER BY {order} {direction} NULLS LAST, i.canonical_symbol
            OFFSET @offset LIMIT @limit
            """);
#pragma warning restore CA2100
        Bind(command);
        command.Parameters.AddWithValue("offset", (search.Page - 1) * search.PageSize);
        command.Parameters.AddWithValue("limit", search.PageSize);

        var items = new List<DataInstrumentRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? first = reader.IsDBNull(5) ? null : IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(5));
            DateTimeOffset? last = reader.IsDBNull(6) ? null : IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(6));
            var csv = reader.GetInt64(8);
            var dhan = reader.GetInt64(9);
            int? behind = last is { } l ? calendar.TradingDaysBetween(IndiaTime.Today(l), expectedSession) : null;
            items.Add(new DataInstrumentRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                first,
                last,
                reader.GetInt64(7),
                last is null ? null : csv > 0 && dhan > 0 ? "mixed" : dhan > 0 ? "dhan" : "csv",
                last is null ? "empty" : behind > 0 ? "behind" : "current",
                behind,
                reader.GetInt32(10)));
        }

        return new DataInstrumentPage(items, search.Page, search.PageSize, total);
    }

    public async ValueTask<BarsInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, canonical_symbol, name FROM market.instruments WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new BarsInstrument(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async ValueTask<IReadOnlyList<BarRow>> ReadBarsAsync(Guid instrumentId, Timeframe timeframe, int take, CancellationToken cancellationToken)
    {
        var minute = timeframe == Timeframe.M1;
        await using var command = dataSource.CreateCommand(minute
            ? "SELECT ts, open, high, low, close, volume, 1, true, source FROM market.bars_1m WHERE instrument_id = @id ORDER BY ts DESC LIMIT @take"
            : "SELECT ts, open, high, low, close, volume, bars, complete, NULL::text FROM market.bars_agg WHERE instrument_id = @id AND timeframe = @timeframe ORDER BY ts DESC LIMIT @take");
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("take", take);
        if (!minute)
        {
            command.Parameters.AddWithValue("timeframe", timeframe.ToCode());
        }

        var rows = new List<BarRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BarRow(
                IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(0)),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetInt64(5),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<BarRow>> ReadDailyBarsAsync(Guid instrumentId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT ts, open, high, low, close, volume, bars, complete, NULL::text FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND ts >= @from AND ts < @to ORDER BY ts");
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        command.Parameters.AddWithValue("to", new DateTimeOffset(toExclusive.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        var rows = new List<BarRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BarRow(
                IndiaTime.ToIst(reader.GetFieldValue<DateTimeOffset>(0)),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetInt64(5),
                reader.GetInt32(6),
                reader.GetBoolean(7),
                null));
        }

        return rows;
    }
}
