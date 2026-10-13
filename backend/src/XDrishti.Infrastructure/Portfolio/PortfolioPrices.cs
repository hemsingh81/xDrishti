using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The stored day closes of many instruments for the portfolio history (ADR 0020): one statement over the <c>1D</c> aggregate for all the instruments and the range (an index lookup per instrument, no per-day or per-holding
/// statements), and one over the applied corporate actions of the same instruments. The closes stay raw; the caller adjusts them on read (ADR 0017 §5).
/// </summary>
internal sealed class PortfolioPrices(NpgsqlDataSource dataSource, XDrishtiDbContext db) : IPortfolioPrices
{
    public async ValueTask<IReadOnlyDictionary<Guid, StoredPrices>> ReadDailyAsync(
        IReadOnlyCollection<Guid> instrumentIds, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, StoredPrices>();
        }

        var closes = new Dictionary<Guid, List<StoredClose>>();
        await using (var command = dataSource.CreateCommand(
            "SELECT instrument_id, ts, close FROM market.bars_agg WHERE instrument_id = ANY(@ids) AND timeframe = '1D' AND ts >= @from AND ts < @to ORDER BY instrument_id, ts"))
        {
            command.Parameters.AddWithValue("ids", ids);
            command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
            command.Parameters.AddWithValue("to", new DateTimeOffset(toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                if (!closes.TryGetValue(id, out var list))
                {
                    closes[id] = list = [];
                }

                list.Add(new StoredClose(IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(1)), reader.GetDouble(2)));
            }
        }

        if (closes.Count == 0)
        {
            return new Dictionary<Guid, StoredPrices>();
        }

        var withBars = closes.Keys.ToArray();
        var applied = (await db.CorporateActions.AsNoTracking()
                .Where(x => withBars.Contains(x.InstrumentId) && x.Status == CorporateActionStatus.Applied)
                .OrderBy(x => x.ExDate)
                .Select(x => new { x.InstrumentId, Action = new AppliedAction(x.ExDate, x.Kind, x.Numerator, x.Denominator, x.Factor) })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.InstrumentId, x => x.Action);
        return closes.ToDictionary(c => c.Key, c => new StoredPrices(c.Value, [.. applied[c.Key]]));
    }
}
