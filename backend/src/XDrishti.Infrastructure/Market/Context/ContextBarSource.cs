using Npgsql;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Infrastructure.Market.Context;

/// <summary>
/// The daily rows of the regime index and the volatility index (P3-01d): the tracked, not retired instruments of type index with a Dhan id in <c>IDX_I</c>, found by canonical symbol in the configured order and then by display name, and their
/// <c>1D</c> rows of <c>market.bars_agg</c>. An index with no daily row is reported as not usable.
/// </summary>
internal sealed class ContextBarSource(NpgsqlDataSource dataSource) : IContextBarSource
{
    public const string IndexName = "Nifty 50";

    public const string VixName = "India VIX";

    public async ValueTask<ContextLegs> ReadAsync(IReadOnlyList<string> indexSymbols, IReadOnlyList<string> vixSymbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexSymbols);
        ArgumentNullException.ThrowIfNull(vixSymbols);
        var candidates = await CandidatesAsync(cancellationToken);
        var index = Pick(candidates, indexSymbols, IndexName);
        var vix = Pick(candidates, vixSymbols, VixName);
        var indexLegs = index is null ? [] : await LegsAsync(index.Id, cancellationToken);
        var vixLegs = vix is null ? [] : await LegsAsync(vix.Id, cancellationToken);
        return new ContextLegs(indexLegs.Count > 0 ? index : null, vixLegs.Count > 0 ? vix : null, indexLegs, vixLegs);
    }

    private static ContextInstrument? Pick(List<(ContextInstrument Instrument, string DisplayName)> candidates, IReadOnlyList<string> symbols, string displayName)
    {
        foreach (var symbol in symbols)
        {
            var hit = candidates.Find(c => string.Equals(c.Instrument.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (hit.Instrument is not null)
            {
                return hit.Instrument;
            }
        }

        var byName = candidates.Find(c => string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        return byName.Instrument;
    }

    private async ValueTask<List<(ContextInstrument Instrument, string DisplayName)>> CandidatesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id, i.canonical_symbol, i.display_name
            FROM market.instruments i
            JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
            WHERE i.type = 'index' AND i.retired_at IS NULL
              AND EXISTS (SELECT 1 FROM market.instrument_broker_ids b WHERE b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment = 'IDX_I')
            ORDER BY i.canonical_symbol
            """);
        var rows = new List<(ContextInstrument, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((new ContextInstrument(reader.GetGuid(0), reader.GetString(1)), reader.GetString(2)));
        }

        return rows;
    }

    private async ValueTask<List<DailyLeg>> LegsAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT ts, high, low, close, bars, complete FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' ORDER BY ts");
        command.Parameters.AddWithValue("id", instrumentId);
        var legs = new List<DailyLeg>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            legs.Add(new DailyLeg(
                IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0)),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetInt32(4),
                reader.GetBoolean(5)));
        }

        return legs;
    }
}
