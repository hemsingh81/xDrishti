using Npgsql;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options;

namespace XDrishti.Infrastructure.OptionQuality;

/// <summary>The reference data of the quality checks: the master list's symbols, the strike step from the contracts of P14-01, the lot history and the rule epochs. Reads no archive row.</summary>
internal sealed class OptionReference(NpgsqlDataSource dataSource) : IOptionReference
{
    /// <summary>The strike step of the two indices whose history predates the stored contracts, in paise: the same figures the backfill labels its bars by (ADR 0177). A share has none known.</summary>
    /// <summary>A stored contract speaks for a date only when its expiry is at most this many days after it: a step read from today's contracts says nothing about 2021 (strike steps are revised).</summary>
    public const int ContractReachDays = 100;

    private static readonly Dictionary<string, int> IndexSteps = new(StringComparer.Ordinal) { ["NSE:NIFTY"] = 5_000, ["NSE:BANKNIFTY"] = 10_000 };

    public async ValueTask<IReadOnlyDictionary<Guid, string>> SymbolsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var symbols = new Dictionary<Guid, string>();
        if (ids.Count == 0)
        {
            return symbols;
        }

        await using var command = dataSource.CreateCommand("SELECT id, canonical_symbol FROM market.instruments WHERE id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", ids.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            symbols[reader.GetGuid(0)] = reader.GetString(1);
        }

        return symbols;
    }

    public async ValueTask<Guid?> FindUnderlyingAsync(string canonicalSymbol, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id FROM market.instruments WHERE canonical_symbol = @s ORDER BY retired_at NULLS FIRST LIMIT 1");
        command.Parameters.AddWithValue("s", canonicalSymbol);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    public async ValueTask<int?> StrikeStepAsync(Guid underlyingId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT (SELECT min(g.gap) FROM (SELECT s.strike_px - lag(s.strike_px) OVER (ORDER BY s.strike_px) AS gap
                                           FROM (SELECT DISTINCT c.strike_px FROM options.contracts c WHERE c.underlying_id = @u AND c.expiry = e.expiry) s) g WHERE g.gap > 0),
                   (SELECT canonical_symbol FROM market.instruments WHERE id = @u)
            FROM (SELECT min(expiry) AS expiry FROM options.contracts WHERE underlying_id = @u AND expiry >= @d AND expiry <= @d + @reach) e
            """);
        command.Parameters.AddWithValue("u", underlyingId);
        command.Parameters.AddWithValue("d", date);
        command.Parameters.AddWithValue("reach", ContractReachDays);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (!reader.IsDBNull(0))
        {
            return reader.GetInt32(0);
        }

        return !reader.IsDBNull(1) && IndexSteps.TryGetValue(reader.GetString(1), out var step) ? step : null;
    }

    public async ValueTask<IReadOnlyList<LotSizeEntry>> LotSizesAsync(Guid underlyingId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, underlying_id, effective_from, lot_size, source, confirmed FROM options.lot_sizes WHERE underlying_id = @u");
        command.Parameters.AddWithValue("u", underlyingId);
        var rows = new List<LotSizeEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LotSizeEntry(reader.GetInt64(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2), reader.GetInt32(3), reader.GetString(4), reader.GetBoolean(5)));
        }

        return rows;
    }

    public async ValueTask<IReadOnlyList<MarketEpoch>> EpochsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT code, effective_from, description, source_url, confirmed FROM options.market_epochs");
        var rows = new List<MarketEpoch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MarketEpoch(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4)));
        }

        return rows;
    }
}
