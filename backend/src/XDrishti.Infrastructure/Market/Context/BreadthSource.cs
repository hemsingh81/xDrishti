using Npgsql;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Infrastructure.Market.Context;

/// <summary>
/// What the breadth is computed from (P3-01e): the tracked, not retired instruments of type <c>share</c> (an ETF would vote with its index), the closes of their complete <c>1D</c> rows (one streamed read per share, the raw <c>close</c>, never the adjusted
/// column) and the ex-dates of their corporate actions of any status. Read only.
/// </summary>
internal sealed class BreadthSource(NpgsqlDataSource dataSource, ICorporateActionQueries actions) : IBreadthSource
{
    public async ValueTask<BreadthData> ReadAsync(DateOnly from, CancellationToken cancellationToken)
    {
        var ids = await ShareIdsAsync(cancellationToken);
        var shares = new List<BreadthInput>(ids.Count);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            shares.Add(new BreadthInput(id, await ClosesAsync(id, from, cancellationToken)));
        }

        var exDates = new Dictionary<Guid, IReadOnlySet<DateOnly>>();
        if (ids.Count > 0)
        {
            foreach (var group in (await actions.ListKeysAsync(ids, cancellationToken)).GroupBy(k => k.InstrumentId))
            {
                exDates[group.Key] = group.Select(k => k.ExDate).ToHashSet();
            }
        }

        return new BreadthData(shares, exDates);
    }

    private async ValueTask<List<Guid>> ShareIdsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.id
            FROM market.instruments i
            JOIN market.tracked_instruments t ON t.instrument_id = i.id AND t.active
            WHERE i.type = 'share' AND i.retired_at IS NULL
            ORDER BY i.id
            """);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async ValueTask<Dictionary<DateOnly, double>> ClosesAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT ts, close FROM market.bars_agg WHERE instrument_id = @id AND timeframe = '1D' AND complete AND ts >= @from ORDER BY ts");
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddDays(-1).ToUniversalTime());
        var closes = new Dictionary<DateOnly, double>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            closes[IndiaTime.Today(reader.GetFieldValue<DateTimeOffset>(0))] = reader.GetDouble(1);
        }

        return closes;
    }
}
