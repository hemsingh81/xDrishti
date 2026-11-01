using Npgsql;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>
/// The underlyings of the backfill (P14-03): the symbols of the app setting <c>options.underlyings</c> (ADR 0175) with their Dhan id from the master list - an index by its <c>IDX_I</c> id, a share by its <c>NSE_EQ</c> id - the shares ranked by the turnover
/// ranking of the setting (the mean close x volume of the last 20 daily bars), best first, and the strike step of NIFTY and BANKNIFTY. A symbol without a Dhan id is left out. Reads reference data only, no bar, no chain row and no option contract.
/// </summary>
internal sealed class BackfillTargets(IOptionUnderlyingSetting setting, NpgsqlDataSource dataSource, OptionBackfillSettings backfill, TimeProvider clock) : IBackfillTargets
{
    /// <summary>The newest daily bar of the ranking may be this many days old at most (the freshness rule of P14-01).</summary>
    public const int MaxBarAgeDays = 7;

    private readonly BackfillStockList _frozen = new(dataSource);

    private const string Query =
        """
        SELECT i.id, i.canonical_symbol, b.segment, b.broker_id
        FROM market.instruments i
        JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment IN ('IDX_I', 'NSE_EQ')
        WHERE i.canonical_symbol = ANY(@symbols) AND i.retired_at IS NULL
        ORDER BY i.canonical_symbol
        """;

    /// <summary>The smallest strike step of the planned indices in paise (P14-01 stores none): NIFTY 50 rupees, BANKNIFTY 100 rupees. A strike that is not a multiple of it is flagged <c>suspect</c> and kept. Shares have none known: unchecked.</summary>
    private static readonly Dictionary<string, int> IndexSteps = new(StringComparer.Ordinal) { ["NSE:NIFTY"] = 5_000, ["NSE:BANKNIFTY"] = 10_000 };

    public async ValueTask<IReadOnlyList<BackfillTarget>> LoadAsync(CancellationToken cancellationToken)
    {
        var listed = OptionUnderlyings.Parse(await setting.ReadAsync(cancellationToken));
        if (listed.IsEmpty)
        {
            return [];
        }

        var found = new List<(Guid Id, string Symbol, long Scrip, bool IsIndex)>();
        await using (var command = dataSource.CreateCommand(Query))
        {
            command.Parameters.AddWithValue("symbols", listed.Symbols.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var segment = reader.GetString(2);
                if (long.TryParse(reader.GetString(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var scrip) && scrip > 0)
                {
                    found.Add((reader.GetGuid(0), reader.GetString(1), scrip, segment == "IDX_I"));
                }
            }
        }

        var stored = await _frozen.ReadAsync(cancellationToken);
        IReadOnlyList<string> order = stored;
        if (stored.Count == 0)
        {
            order = (await Ranked(cancellationToken)).Symbols;
        }

        var rank = order.Select((symbol, index) => (symbol, index)).ToDictionary(x => x.symbol, x => x.index, StringComparer.Ordinal);
        BackfillTarget Make((Guid Id, string Symbol, long Scrip, bool IsIndex) t) => new(t.Id, t.Symbol, t.Scrip, t.IsIndex, t.IsIndex && IndexSteps.TryGetValue(t.Symbol, out var step) ? step : null);
        var indices = found.Where(t => t.IsIndex).Select(Make);
        var stocks = found.Where(t => !t.IsIndex).OrderBy(t => rank.TryGetValue(t.Symbol, out var r) ? r : int.MaxValue).ThenBy(t => t.Symbol, StringComparer.Ordinal).Select(Make);
        return [.. indices, .. stocks];
    }

    public async ValueTask FreezeStocksAsync(CancellationToken cancellationToken)
    {
        if ((await _frozen.ReadAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        var listed = OptionUnderlyings.Parse(await setting.ReadAsync(cancellationToken));
        var all = await Ranked(cancellationToken);
        var ranked = all.Symbols.Where(s => listed.Symbols.Contains(s, StringComparer.Ordinal)).ToArray();
        var wanted = Math.Min(backfill.StockCount, listed.Symbols.Count(s => !IndexSteps.ContainsKey(s) && !OptionUnderlyings.DefaultIndices.Contains(s, StringComparer.Ordinal)));
        var today = IndiaTime.Today(clock.GetUtcNow());

        // Frozen only when the ranking is complete and fresh (the rule of P14-01); an incomplete or stale ranking would fix a wrong plan for good.
        if (ranked.Length > 0 && ranked.Length >= wanted && all.NewestBar is { } newest && newest >= today.AddDays(-MaxBarAgeDays))
        {
            await _frozen.WriteOnceAsync(ranked, "service:options-backfill", clock.GetUtcNow(), cancellationToken);
        }
    }

    private ValueTask<RankedShares> Ranked(CancellationToken cancellationToken) =>
        setting.MostTradedFnoSharesAsync(IndiaTime.Today(clock.GetUtcNow()), OptionUnderlyings.DefaultShares, cancellationToken);
}
