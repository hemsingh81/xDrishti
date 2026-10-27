using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Assistant;

/// <summary>
/// The portfolio figures of the assistant's <c>get_portfolio</c> (spec P9-05 AC 6): sums and groups over the rows of the <c>aiv</c> views, computed with the very calculator of the Holdings screen (<see cref="ReturnsCalculator"/>, four decimals, away from zero, the same XIRR), so that a figure on the
/// screen and the same figure in an answer cannot differ. A holding without a usable price has no value, profit or XIRR (the calculator's rule); a group whose cash flows are too short has XIRR <c>insufficient</c>. Pure.
/// </summary>
public static class PortfolioFigures
{
    public const string None = "none";
    public const string Bucket = "bucket";
    public const string Basket = "basket";
    public const string Sector = "sector";
    public const string Account = "account";

    /// <summary>The groups for <paramref name="groupBy"/> (none, bucket, basket, sector, account), each as a JSON object of metric-shaped figures; the second value is true when a holding sits in more than one group (baskets).</summary>
    public static (IReadOnlyList<JsonObject> Groups, bool NonAdditive) Compute(IReadOnlyList<PortfolioHoldingRow> holdings, string groupBy, DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        var buckets = new Dictionary<string, List<PortfolioHoldingRow>>(StringComparer.Ordinal);
        var memberships = 0;
        foreach (var holding in holdings)
        {
            foreach (var key in KeysOf(holding, groupBy))
            {
                if (!buckets.TryGetValue(key, out var list))
                {
                    buckets[key] = list = [];
                }

                list.Add(holding);
                memberships++;
            }
        }

        var groups = buckets.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => Group(b.Key, b.Value, valuationDate)).ToList();
        return (groups, memberships > holdings.Count);
    }

    private static IEnumerable<string> KeysOf(PortfolioHoldingRow h, string groupBy) => groupBy switch
    {
        Account => [h.Account],
        Bucket => h.Buckets.Count == 0 ? ["unclassified"] : h.Buckets,
        Basket => h.Baskets.Count == 0 ? ["no basket"] : h.Baskets,
        Sector => [string.IsNullOrWhiteSpace(h.Sector) ? "unclassified" : h.Sector],
        _ => ["all"],
    };

    private static JsonObject Group(string key, List<PortfolioHoldingRow> rows, DateOnly valuationDate)
    {
        var data = rows.Select(r => ToData(r)).ToList();
        var figures = ReturnsCalculator.ForGroup(data, valuationDate);
        var unpriced = data.Count(h => !h.IsPriced);
        if (unpriced == data.Count)
        {
            // Nothing in the group has a usable price: the cost is known, the worth is not, and no figure is made up from it.
            return new JsonObject
            {
                ["group"] = key,
                ["holdings"] = rows.Count,
                ["unpricedHoldings"] = unpriced,
                ["invested"] = Unavailable("no holding of the group has a usable last price"),
                ["value"] = Unavailable("no usable last price"),
                ["unrealised"] = Unavailable("no usable last price"),
                ["unrealisedPct"] = Unavailable("no usable last price"),
                ["xirr"] = Unavailable("no usable last price"),
                ["note"] = $"{unpriced} holding(s) have no usable last price and are left out of value, unrealised and xirr",
            };
        }

        var group = new JsonObject
        {
            ["group"] = key,
            ["holdings"] = rows.Count,
            ["unpricedHoldings"] = unpriced,
            ["invested"] = Value(figures.Invested),
            ["value"] = figures.Value is { } v ? Value(v) : Unavailable("no usable last price"),
            ["unrealised"] = figures.Pnl is { } p ? Value(p) : Unavailable("no usable last price"),
            ["unrealisedPct"] = figures.PnlPct is { } pct ? Value(pct) : Unavailable(figures.Invested > 0m ? "no usable last price" : "nothing invested"),
            ["xirr"] = XirrNode(figures.Xirr),
        };
        if (unpriced > 0)
        {
            group["note"] = $"{unpriced} holding(s) have no usable last price and are left out of value, unrealised and xirr";
        }

        return group;
    }

    private static JsonObject XirrNode(XirrResult xirr) => xirr.State switch
    {
        XirrState.Ok when xirr.Rate is { } r => new JsonObject { ["state"] = "value", ["number"] = Math.Round(r * 100.0, 2, MidpointRounding.AwayFromZero), ["unit"] = "percent per year" },
        XirrState.NotMeaningful => Unavailable("not meaningful (beyond 1,000 percent a year)", "not_evaluable"),
        XirrState.NoPrice => Unavailable("no usable last price"),
        XirrState.NoData => Unavailable("no cash flows", "empty"),
        _ => new JsonObject { ["state"] = "insufficient", ["reason"] = $"fewer than {Xirr.MinDays} days between the first and last cash flow, or no outflow or inflow" },
    };

    private static JsonObject Value(decimal number) => new() { ["state"] = "value", ["number"] = JsonValue.Create(Normalise(number)) };

    private static JsonObject Unavailable(string reason, string state = "unavailable") => new() { ["state"] = state, ["reason"] = reason };

    /// <summary>Four decimals without trailing zeros (1000.0000 is written 1000).</summary>
    private static decimal Normalise(decimal value) => value / 1.0000000000000000000000000000m;

    private static HoldingData ToData(PortfolioHoldingRow r) =>
        new(
            Guid.Empty, Guid.Empty, r.Account, Guid.Empty, string.Empty, null, null, string.Empty, string.Empty, null, r.Symbol, r.Symbol, r.Qty, r.AvgCost, r.LastPrice, r.LastPriceAt, r.Status, DateTimeOffset.MinValue, null, DateTimeOffset.MinValue,
            [.. r.Lots.Select(l => new LotData(Guid.Empty, l.AcquiredOn, l.Qty, l.UnitCost, "import", false, l.ClosedOn, l.ClosePrice, l.Estimated))]);
}
