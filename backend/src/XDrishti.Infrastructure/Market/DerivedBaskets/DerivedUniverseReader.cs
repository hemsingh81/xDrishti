using Npgsql;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Infrastructure.Market.DerivedBaskets;

/// <summary>
/// The universe of the derived baskets (BK-03), read from our own tables only (no network): every share with complete daily rows (<c>1D</c> of <c>market.bars_agg</c>) in the window, <b>retired ones included</b>, in one query. The raw prices are adjusted by the applied corporate actions
/// (bars before the ex-date times the factor, volume divided by it; an action on or before the date the owner's CSV files reach is skipped, those files are adjusted already). An unresolved suggestion with an ex-date in the window marks the instrument <c>CorporateActionUnresolved</c>.
/// The Nifty 50 (basket 5 and 14) comes from the context source. The point-in-time flags of basket 13 come from the dated list baskets of BK-05 (ASM/GSM, ban, trade-for-trade); until all three have been recorded there are none (a missing flag is not a pass).
/// </summary>
internal sealed class DerivedUniverseReader(NpgsqlDataSource dataSource, ICorporateActionQueries actions, IContextBarSource context, IContextSettings contextSettings) : IDerivedUniverseReader
{
    public async ValueTask<IReadOnlyList<UniverseInstrument>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await BarsAsync(from, to, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var csvThrough = await actions.ListCsvAdjustedThroughAsync(ids, cancellationToken);
        var corporate = await ActionsAsync(ids, from, to, cancellationToken);
        var legs = await context.ReadAsync(contextSettings.IndexSymbols, contextSettings.VixSymbols, cancellationToken);
        var benchmark = legs.IndexLegs.Where(l => l.Complete && l.Date >= from && l.Date <= to).OrderBy(l => l.Date).Select(l => new Bar(l.Date, l.Close, l.High, l.Low, l.Close, 0)).ToArray();

        var flags = await FlagsAsync(ids, cancellationToken);
        var result = new List<UniverseInstrument>(rows.Count);
        foreach (var row in rows)
        {
            var applied = corporate.TryGetValue(row.Id, out var list) ? list : [];
            var skipThrough = csvThrough.TryGetValue(row.Id, out var through) ? through : (DateOnly?)null;
            var daily = row.Bars.Select(b => Adjust(b, applied.Where(a => a.Applied && (skipThrough is null || a.ExDate > skipThrough)).ToList())).ToArray();
            var tick = row.TickPaise is { } paise && paise > 0 ? (double)paise / 100.0 : 0.05;
            var dayFlags = flags is null ? null : DayFlagBuilder.Build(
                flags.Since, row.Series, flags.Stays(ListBaskets.AsmGsm.Id, row.Id), flags.Stays(ListBaskets.Ban.Id, row.Id), flags.Stays(ListBaskets.TradeForTrade.Id, row.Id));
            result.Add(new UniverseInstrument(row.Id, new InstrumentBars(
                row.Symbol, daily, null, dayFlags, benchmark.Length > 0 ? benchmark : null, applied.Any(a => !a.Applied), row.Sector, tick)));
        }

        return result;
    }

    private static Bar Adjust(Bar bar, List<CorporateAction> applied)
    {
        var factor = 1.0;
        foreach (var action in applied.Where(a => bar.Date < a.ExDate))
        {
            factor *= action.Factor;
        }

        return factor == 1.0 ? bar : new Bar(bar.Date, bar.Open * factor, bar.High * factor, bar.Low * factor, bar.Close * factor, bar.Volume / factor);
    }

    private async ValueTask<List<InstrumentRows>> BarsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id, i.canonical_symbol, i.sector, i.tick_size, a.ts, a.open, a.high, a.low, a.close, a.volume, i.series
            FROM market.instruments i
            JOIN market.bars_agg a ON a.instrument_id = i.id AND a.timeframe = '1D' AND a.complete AND a.ts >= @from AND a.ts < @to
            WHERE i.type = 'share'
            ORDER BY i.id, a.ts
            """);
        command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        command.Parameters.AddWithValue("to", new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        var rows = new List<InstrumentRows>();
        InstrumentRows? current = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            if (current is null || current.Id != id)
            {
                var symbol = reader.GetString(1);
                current = new InstrumentRows(
                    id, symbol.StartsWith("NSE:", StringComparison.Ordinal) ? symbol[4..] : symbol, reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3), reader.IsDBNull(10) ? null : reader.GetString(10));
                rows.Add(current);
            }

            current.Bars.Add(new Bar(IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(4)), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8), reader.GetInt64(9)));
        }

        return rows;
    }

    private async ValueTask<Dictionary<Guid, List<CorporateAction>>> ActionsAsync(List<Guid> ids, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT instrument_id, ex_date, factor, status FROM market.corporate_actions WHERE instrument_id = ANY(@ids) AND status IN ('applied', 'suggested') AND ex_date > @from");
        command.Parameters.AddWithValue("ids", ids.ToArray());
        command.Parameters.AddWithValue("from", from);
        var result = new Dictionary<Guid, List<CorporateAction>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var exDate = reader.GetFieldValue<DateOnly>(1);
            var applied = reader.GetString(3) == "applied";
            if (!applied && exDate > to)
            {
                continue; // a suggestion after the window does not touch it
            }

            if (!result.TryGetValue(id, out var list))
            {
                result[id] = list = [];
            }

            list.Add(new CorporateAction(exDate, (double)reader.GetDecimal(2), applied));
        }

        return result;
    }

    /// <summary>
    /// The dated stays of the three flag lists and the first date all three are known (BK-05): each list needs one recorded check (an applied or unchanged fetch row); the ban list, whose file is for the next trading day, is known from the day after its first check.
    /// Null when any list has never been recorded.
    /// </summary>
    private async ValueTask<FlagStays?> FlagsAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var lists = new[] { ListBaskets.AsmGsm.Id, ListBaskets.Ban.Id, ListBaskets.TradeForTrade.Id };
        var first = new Dictionary<Guid, DateOnly>();
        await using (var command = dataSource.CreateCommand(
            "SELECT basket_id, min(fetched_at) FROM market.basket_fetches WHERE basket_id = ANY(@lists) AND outcome IN ('applied', 'unchanged') GROUP BY basket_id"))
        {
            command.Parameters.AddWithValue("lists", lists);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                first[reader.GetGuid(0)] = IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(1));
            }
        }

        if (lists.Any(l => !first.ContainsKey(l)))
        {
            return null;
        }

        var since = new[] { first[ListBaskets.AsmGsm.Id], first[ListBaskets.Ban.Id].AddDays(1), first[ListBaskets.TradeForTrade.Id] }.Max();
        var stays = new Dictionary<(Guid List, Guid Instrument), List<ListStay>>();
        await using (var command = dataSource.CreateCommand("SELECT basket_id, instrument_id, valid_from, valid_to FROM market.basket_members WHERE basket_id = ANY(@lists) AND instrument_id = ANY(@ids)"))
        {
            command.Parameters.AddWithValue("lists", lists);
            command.Parameters.AddWithValue("ids", ids.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = (reader.GetGuid(0), reader.GetGuid(1));
                if (!stays.TryGetValue(key, out var list))
                {
                    stays[key] = list = [];
                }

                list.Add(new ListStay(IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(2)), reader.IsDBNull(3) ? null : IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(3))));
            }
        }

        return new FlagStays(since, stays);
    }

    private sealed record FlagStays(DateOnly Since, Dictionary<(Guid List, Guid Instrument), List<ListStay>> All)
    {
        public List<ListStay> Stays(Guid list, Guid instrument) => All.TryGetValue((list, instrument), out var found) ? found : [];
    }

    private sealed record CorporateAction(DateOnly ExDate, double Factor, bool Applied);

    private sealed class InstrumentRows(Guid id, string symbol, string? sector, decimal? tickPaise, string? series)
    {
        public Guid Id { get; } = id;

        public string Symbol { get; } = symbol;

        public string? Sector { get; } = sector;

        public decimal? TickPaise { get; } = tickPaise;

        public string? Series { get; } = series;

        public List<Bar> Bars { get; } = [];
    }
}
