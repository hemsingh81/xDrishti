using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Actions.Rules;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Application.Tests.Portfolio.Groups;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Made-up positions and rule contexts for the Action-center tests. "Today" is Monday 2026-10-12 (India), the same day as the grouping tests.</summary>
internal static class ActionFixtures
{
    public static readonly DateOnly Today = new(2026, 10, 12);

    public static readonly Guid AccountOne = GroupFixtures.AccountOne;

    public static readonly Guid AccountTwo = GroupFixtures.AccountTwo;

    public static readonly Guid BucketA = GroupFixtures.BucketA;

    public static readonly Guid BucketB = GroupFixtures.BucketB;

    /// <summary>A stable instrument id for a symbol (the master is made up).</summary>
    public static Guid InstrumentOf(string symbol) => new($"00000000-0000-7000-8000-{symbol.Aggregate(7L, (a, c) => ((a * 31) + c) % 0xFFFFFFFFFFL):x12}");

    /// <summary>An open position: <paramref name="qty"/> shares bought at <paramref name="cost"/> on <paramref name="acquired"/>, now at <paramref name="price"/> (0: unpriced). In the master unless <paramref name="mapped"/> is false.</summary>
    public static HoldingData Pos(string symbol, decimal price, long qty = 10, decimal cost = 100m, DateOnly? acquired = null, Guid? account = null, Guid? bucket = null, bool mapped = true) =>
        GroupFixtures.Held(symbol, price, qty, cost, acquired ?? new DateOnly(2026, 8, 1), account ?? AccountOne, bucket ?? BucketA, mapped ? InstrumentOf(symbol) : null);

    /// <summary>The position with its lots replaced.</summary>
    public static HoldingData WithLots(this HoldingData holding, params LotData[] lots) => holding with { Lots = lots, Qty = lots.Where(l => l.IsOpen).Sum(l => l.Qty) };

    public static ActionContext Context(
        IEnumerable<HoldingData> holdings,
        ActionThresholds? thresholds = null,
        IReadOnlyDictionary<string, string>? sectors = null,
        IEnumerable<string>? inBasket = null,
        DateOnly? today = null) =>
        new(
            [.. holdings],
            today ?? Today,
            thresholds ?? ActionThresholds.Defaults,
            (sectors ?? new Dictionary<string, string>()).ToDictionary(s => InstrumentOf(s.Key), s => s.Value),
            (inBasket ?? []).Select(InstrumentOf).ToHashSet());

    /// <summary>Items of one rule by symbol (account-level items by account name).</summary>
    public static Dictionary<string, RuleHit> BySymbol(IEnumerable<RuleHit> hits) => hits.ToDictionary(h => h.Item.Symbol ?? h.Item.AccountName);

    public static DailyClose[] Index(params (DateOnly Date, decimal Close)[] closes) => [.. closes.Select(c => new DailyClose(c.Date, c.Close))];
}
