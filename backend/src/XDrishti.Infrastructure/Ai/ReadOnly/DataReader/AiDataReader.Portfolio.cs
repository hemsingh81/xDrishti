using System.Globalization;
using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Assistant;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

internal sealed partial class AiDataReader
{
    private const string PortfolioOff = "portfolio context is off";

    public async Task<AiToolData> GetPortfolioAsync(string groupBy, bool includeClosed, CancellationToken cancellationToken)
    {
        var state = await context.GetAsync(cancellationToken);
        if (!state.PortfolioAllowed)
        {
            // The switch is read first and no portfolio row is read while it is off.
            return new AiToolData([], null, null, null, false, new ToolEvidence("the owner's holdings", [], null, null, [], [new ToolUnavailable("portfolio", PortfolioOff)]), ["unavailable: " + PortfolioOff]);
        }

        var holdings = await QueryAsync("SELECT account, symbol, qty, avg_cost, last_price, last_price_at, status, instrument_id FROM aiv.holdings WHERE (@closed OR status = 'open') ORDER BY account, symbol LIMIT 5000", cancellationToken, ("closed", includeClosed));
        var lots = (await QueryAsync("SELECT account, symbol, acquired_on, qty, unit_cost, closed_on, close_price, estimated FROM aiv.holding_lots LIMIT 50000", cancellationToken))
            .GroupBy(l => ((string?)l[0] ?? string.Empty, (string)l[1]!))
            .ToDictionary(g => g.Key, g => g.Select(l => new PortfolioLotRow(DateOnly.Parse((string)l[2]!, CultureInfo.InvariantCulture), Convert.ToInt64(l[3], CultureInfo.InvariantCulture), Money(l[4]), l[5] is string c ? DateOnly.Parse(c, CultureInfo.InvariantCulture) : null, l[6] is null ? null : Money(l[6]), (bool)l[7]!)).ToList());

        var ids = holdings.Where(h => h[7] is string).Select(h => Guid.Parse((string)h[7]!)).Distinct().ToArray();
        var instruments = ids.Length == 0 ? [] : await QueryAsync("SELECT id, symbol, sector FROM aiv.instruments WHERE id = ANY(@ids)", cancellationToken, ("ids", ids));
        var byInstrument = instruments.ToDictionary(i => (string)i[0]!, i => (Symbol: (string)i[1]!, Sector: i[2] as string), StringComparer.Ordinal);
        var canonical = byInstrument.Values.Select(v => v.Symbol).Distinct(StringComparer.Ordinal).ToArray();
        var memberships = canonical.Length == 0
            ? []
            : (await QueryAsync("SELECT m.symbol, m.basket FROM aiv.basket_members m JOIN aiv.baskets b ON b.id = m.basket_id WHERE m.valid_to IS NULL AND b.archived_at IS NULL AND m.symbol = ANY(@s)", cancellationToken, ("s", canonical)))
                .GroupBy(m => (string)m[0]!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(m => (string)m[1]!).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var buckets = (await QueryAsync("SELECT account, symbol, bucket FROM aiv.holding_groups", cancellationToken))
            .Where(b => b[2] is string)
            .GroupBy(b => ((string?)b[0] ?? string.Empty, (string)b[1]!))
            .ToDictionary(g => g.Key, g => g.Select(b => (string)b[2]!).Distinct(StringComparer.Ordinal).ToList());

        var rows = holdings.Select(h =>
        {
            var account = (string?)h[0] ?? string.Empty;
            var symbol = (string)h[1]!;
            var known = h[7] is string id && byInstrument.TryGetValue(id, out var instrument);
            var info = known ? byInstrument[(string)h[7]!] : default;
            return new PortfolioHoldingRow(
                account, symbol, Convert.ToInt64(h[2], CultureInfo.InvariantCulture), Money(h[3]), Money(h[4]), h[5] is string at ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) : null, (string)h[6]!,
                info.Sector, buckets.GetValueOrDefault((account, symbol), []), known ? memberships.GetValueOrDefault(info.Symbol, []) : [], lots.GetValueOrDefault((account, symbol), []));
        }).ToList();

        var today = TodayInIndia();
        var (groups, nonAdditive) = PortfolioFigures.Compute(rows, groupBy, today);
        var (totals, _) = PortfolioFigures.Compute(rows, PortfolioFigures.None, today);
        var columns = new AiToolColumn[] { new("group", groupBy != PortfolioFigures.None), new("holdings", false), new("unpricedHoldings", false), new("invested", false), new("value", false), new("unrealised", false), new("unrealisedPct", false), new("xirr", false) };
        var shaped = groups.Select(g => (IReadOnlyList<object?>)[g["group"]!.GetValue<string>(), g["holdings"]!.GetValue<int>(), g["unpricedHoldings"]!.GetValue<int>(), g["invested"]!.DeepClone(), g["value"]!.DeepClone(), g["unrealised"]!.DeepClone(), g["unrealisedPct"]!.DeepClone(), g["xirr"]!.DeepClone()]).ToList();
        var priced = rows.Where(r => r.LastPriceAt is not null).Select(r => r.LastPriceAt!.Value).ToList();
        var data = new JsonObject
        {
            ["groupBy"] = groupBy,
            ["valuationDate"] = Day(today),
            ["nonAdditive"] = nonAdditive,
            ["totals"] = totals.Count == 1 ? totals[0].DeepClone() : null,
            ["priceAsOfOldest"] = priced.Count == 0 ? null : priced.Min().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["priceAsOfNewest"] = priced.Count == 0 ? null : priced.Max().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        return AiToolData.OfRows(
            columns,
            shaped,
            new ToolEvidence("stored holdings valued at the stored last price (price-only, gross)", ["aiv.holdings", "aiv.holding_lots"], null, rows.Count, ["unrealised excludes dividends and charges", "the last price is the stored one (sync, quote or live) with its time; a stale price is shown, not repaired", "xirr needs at least 30 days between the first and last cash flow", "no cash or funds figure exists in this answer"], []),
            nonAdditive ? ["a holding can sit in more than one basket, so the groups must not be added together"] : [],
            data);
    }

    private static decimal Money(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
