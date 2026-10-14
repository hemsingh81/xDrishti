using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Live.Push;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Application.Tests.Portfolio.Groups;
using XDrishti.Application.Tests.Portfolio.History;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Live.Golden;

/// <summary>
/// Runs the real handlers (holding rows, summary, holding groups) over holdings on in-memory ports and writes a price pushed over the hub into them with the real write rule (<see cref="LivePricePolicy"/>). The clock is Wednesday
/// 2026-10-14 10:35:44 India time. XIRR is left out of the golden (a floating-point result the browser never works out): every <c>xirr</c> is null.
/// </summary>
internal static class GoldenWorld
{
    public static readonly DateOnly Valuation = new(2026, 10, 14);

    public const string Unclassified = "Unclassified";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 5, 5, 44, TimeSpan.Zero);

    public static Guid Id(string kind, int n) => Guid.Parse($"00000000-0000-4000-8000-{kind}{n:D10}");

    public static DateTimeOffset Ist(int day, int hour, int minute, int second = 0) =>
        new DateTimeOffset(2026, 10, day, hour, minute, second, TimeSpan.FromHours(5.5)).ToUniversalTime();

    public static GoldenScenario Scenario(string name, string description, IReadOnlyList<GoldenHolding> holdings, IReadOnlyList<InstrumentPrice> pushes)
    {
        var after = holdings.Select(h => pushes.Aggregate(h, Apply)).ToList();
        return new GoldenScenario(name, description, holdings, pushes, StateOf(holdings), StateOf(after));
    }

    /// <summary>The holding after a price pushed for its instrument: the write the feed (live) or the quote poll (quote) does, which only an open holding takes and only when the price policy says the new price wins.</summary>
    public static GoldenHolding Apply(GoldenHolding holding, InstrumentPrice push)
    {
        if (holding.InstrumentId != push.InstrumentId || holding.Status != "open")
        {
            return holding;
        }

        var held = Enum.Parse<PriceSource>(holding.PriceSource, ignoreCase: true);
        if (push.Source == "quote")
        {
            // A quote with a previous close writes it with the fetch time; the price is kept when a newer live price is held.
            var replaces = LivePricePolicy.QuoteReplaces(held, holding.LastPriceAt, push.PriceAt);
            var withClose = holding.PreviousClose is null ? holding : holding with { PreviousCloseAt = push.PriceAt };
            return replaces ? withClose with { LastPrice = push.LastPrice, LastPriceAt = push.PriceAt, PriceSource = "quote" } : withClose;
        }

        return LivePricePolicy.LiveReplaces(held, holding.LastPriceAt, push.PriceAt, holding.LastPrice, push.LastPrice)
            ? holding with { LastPrice = push.LastPrice, LastPriceAt = push.PriceAt, PriceSource = "live" }
            : holding;
    }

    private static GoldenState StateOf(IReadOnlyList<GoldenHolding> holdings)
    {
        var data = holdings.Select(Data).ToArray();
        var queries = new StubHoldingQueries(data);
        var classifications = new FakeHoldingClassifications();
        foreach (var holding in holdings.Where(h => h.InstrumentId is not null && h.Sector != Unclassified))
        {
            classifications.Sectors[holding.InstrumentId!.Value] = holding.Sector;
        }

        var clock = new FakeTimeProvider(Now);
        var groups = new GetHoldingGroupsHandler(queries, classifications, clock, NullLogger<GetHoldingGroupsHandler>.Instance);
        var summary = new GetHoldingsSummaryHandler(
            queries, Substitute.For<IHoldingSyncStore>(), Substitute.For<IAccountStore>(), Substitute.For<IPortfolioBenchmark>(), Substitute.For<IPortfolioPrices>(), new FakeTradingCalendar(HistoryData.Weekdays), clock);

        var read = summary.HandleAsync(new GetHoldingsSummaryQuery(null, null), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return new GoldenState(
            [.. data.Select(h => NoXirr(HoldingViews.Row(h, Valuation)))],
            read.Value with { Returns = read.Value.Returns with { Xirr = null } },
            new GoldenGroupings(Set(groups, "all"), Set(groups, "open")),
            Members(data, classifications));
    }

    private static GoldenGroupSet Set(GetHoldingGroupsHandler handler, string status) =>
        new(Groups(handler, "account", status), Groups(handler, "bucket", status), Groups(handler, "sector", status));

    private static HoldingGroups Groups(GetHoldingGroupsHandler handler, string by, string status)
    {
        var result = handler.HandleAsync(new GetHoldingGroupsQuery(new HoldingGroupsRequest(null, null, status, null, by, null, null)), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var groups = result.Value;
        return groups with
        {
            Groups = [.. groups.Groups.Select(g => g with { Xirr = null })],
            Total = groups.Total with { Xirr = null },
        };
    }

    private static HoldingRow NoXirr(HoldingRow row) => row with { Returns = row.Returns with { Xirr = null } };

    /// <summary>The open holdings of each sector group, the lists the members endpoint gives (the server's own partition).</summary>
    private static SortedDictionary<string, IReadOnlyList<Guid>> Members(IReadOnlyList<HoldingData> holdings, FakeHoldingClassifications classifications)
    {
        var classification = new Classification(classifications.Sectors, new Dictionary<Guid, IReadOnlyList<BasketRef>>());
        var members = new SortedDictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal);
        foreach (var slice in HoldingPartitions.Partition(holdings, GroupDimension.Sector, classification))
        {
            members[slice.Key] = [.. slice.Holdings.Where(h => h.IsOpen).Select(h => h.Id)];
        }

        return members;
    }

    private static HoldingData Data(GoldenHolding h)
    {
        var open = h.Lots.Where(l => l.ClosedOn is null).ToList();
        var openQty = open.Sum(l => l.Qty);
        var average = openQty == 0 ? 0m : Math.Round(open.Sum(l => l.Qty * l.UnitCost) / openQty, 4, MidpointRounding.AwayFromZero);
        return new HoldingData(
            h.Id,
            h.AccountId,
            "Account " + h.AccountId.ToString()[^2..],
            h.BucketId,
            "Bucket " + h.BucketId.ToString()[^2..],
            h.InstrumentId,
            h.InstrumentId is null ? null : h.Symbol,
            "NSE",
            h.Id.ToString()[^6..],
            null,
            h.Symbol,
            h.Symbol + " Limited",
            h.Qty,
            average,
            h.LastPrice,
            h.LastPriceAt,
            h.Status,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            h.Status == "closed" ? new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) : null,
            new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero),
            [.. h.Lots.Select(l => new LotData(Guid.NewGuid(), l.AcquiredOn, l.Qty, l.UnitCost, "import", false, l.ClosedOn, l.ClosePrice, false))],
            h.PreviousClose,
            h.PriceSource,
            h.PreviousCloseAt);
    }
}
