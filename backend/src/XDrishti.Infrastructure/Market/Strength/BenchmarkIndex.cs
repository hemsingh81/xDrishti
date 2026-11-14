using Npgsql;
using XDrishti.Application.Abstractions.Market.Strength;

namespace XDrishti.Infrastructure.Market.Strength;

/// <summary>Finds the tracked Nifty 50 index instrument the relative-strength features measure against (BK-09): by canonical symbol, then by display name.</summary>
internal sealed class BenchmarkIndex(NpgsqlDataSource dataSource) : IBenchmarkIndex
{
    public const string Symbol = "NSE:NIFTY";

    public const string DisplayName = "Nifty 50";

    public async ValueTask<Guid?> FindAsync(CancellationToken cancellationToken)
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
        var rows = new List<(Guid Id, string Symbol, string Name)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        var bySymbol = rows.Find(r => string.Equals(r.Symbol, Symbol, StringComparison.OrdinalIgnoreCase));
        if (bySymbol != default)
        {
            return bySymbol.Id;
        }

        var byName = rows.Find(r => string.Equals(r.Name, DisplayName, StringComparison.OrdinalIgnoreCase));
        return byName == default ? null : byName.Id;
    }
}
