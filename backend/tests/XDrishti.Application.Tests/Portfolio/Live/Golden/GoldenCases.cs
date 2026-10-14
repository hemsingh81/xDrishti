using XDrishti.Application.Live.Push;
using static XDrishti.Application.Tests.Portfolio.Live.Golden.GoldenWorld;

namespace XDrishti.Application.Tests.Portfolio.Live.Golden;

/// <summary>The cases of the golden file: hand-made ones that name each rule, and seeded random ones with many rows.</summary>
internal static class GoldenCases
{
    private static readonly Guid AccountOne = Id("aa", 0);
    private static readonly Guid AccountTwo = Id("aa", 1);
    private static readonly Guid AccountThree = Id("aa", 2);
    private static readonly Guid BucketOne = Id("bb", 0);
    private static readonly Guid BucketTwo = Id("bb", 1);

    public static IReadOnlyList<GoldenScenario> All() =>
    [
        SourcesAndCloseDays(),
        PushOrdering(),
        UnpricedAndUnmapped(),
        NoPreviousCloseAnywhere(),
        GoldenGenerator.Scenario("many-rows-sub-paise", "150 holdings of 45 instruments in 4 accounts and 3 buckets, prices to four decimals, so sums of rounded rows and rounded sums can differ.", 20261014, 150, 45, 4, 3),
        GoldenGenerator.Scenario("random-1", "25 random holdings.", 1, 25, 12, 3, 2),
        GoldenGenerator.Scenario("random-2", "25 random holdings.", 2, 25, 12, 3, 2),
        GoldenGenerator.Scenario("random-3", "25 random holdings.", 3, 25, 8, 2, 2),
    ];

    private static GoldenLot Open(long qty, decimal cost, int daysAgo = 400) => new(qty, cost, new DateOnly(2026, 10, 14).AddDays(-daysAgo), null, null);

    private static GoldenLot Closed(long qty, decimal cost, decimal close) => new(qty, cost, new DateOnly(2025, 3, 3), new DateOnly(2026, 6, 15), close);

    private static GoldenHolding Held(
        int n, string symbol, Guid account, Guid bucket, int? instrument, string sector, long qty, decimal price, DateTimeOffset? at, string source, decimal? close, DateTimeOffset? closeAt, params GoldenLot[] lots) =>
        new(Id("11", n), account, bucket, instrument is null ? null : Id("ee", instrument.Value), sector, symbol, qty, lots.All(l => l.ClosedOn is not null) ? "closed" : "open", price, at, source, close, closeAt, lots);

    private static InstrumentPrice Push(int instrument, decimal price, DateTimeOffset at, string source = "live") => new(Id("ee", instrument), price, at, source);

    private static GoldenScenario SourcesAndCloseDays()
    {
        var yesterday = Ist(13, 15, 30);
        var holdings = new List<GoldenHolding>
        {
            Held(1, "RELQ", AccountOne, BucketOne, 1, "Energy", 10, 2900m, Ist(14, 10, 30), "quote", 2880m, Ist(14, 10, 30), Open(10, 2500m)),
            Held(2, "TCSL", AccountOne, BucketOne, 2, "IT", 10, 3880m, Ist(14, 10, 35, 30), "live", 3900m, Ist(14, 10, 30), Open(10, 4000m)),
            Held(3, "INFL", AccountOne, BucketTwo, 3, "IT", 25, 1500.1234m, Ist(14, 10, 35), "live", 1490m, yesterday, Open(25, 1400.5m)),
            Held(4, "HDFL", AccountTwo, BucketOne, 4, "Banks", 40, 1650m, Ist(14, 10, 20), "live", 1640m, null, Open(40, 1500m)),
            Held(5, "SBIL", AccountTwo, BucketOne, 5, "Banks", 100, 612.35m, yesterday, "sync", 600.5m, null, Open(100, 580m)),
            Held(6, "ITCL", AccountTwo, BucketTwo, 6, "Energy", 200, 450m, yesterday, "sync", null, null, Open(200, 400m)),
            Held(7, "NEWL", AccountOne, BucketTwo, 7, "Pharma", 50, 0m, null, "sync", null, null, Open(50, 100m)),
            Held(8, "SMLL", AccountOne, BucketOne, null, "Unclassified", 100, 60m, yesterday, "sync", 58m, null, Open(100, 50m)),
            Held(9, "PRTL", AccountThree, BucketOne, 9, "Pharma", 20, 105.5m, Ist(14, 10, 31), "quote", 101.25m, Ist(14, 10, 31), Open(20, 100.1234m), Closed(5, 90m, 120m)),
            Held(10, "SOLD", AccountThree, BucketOne, 10, "Metals", 0, 150m, yesterday, "sync", null, null, Closed(10, 100m, 150m), Closed(3, 120.5m, 99.75m)),
            Held(11, "QTYD", AccountThree, BucketTwo, 11, "Metals", 12, 77.77m, Ist(14, 10, 32), "quote", 76.5m, Ist(14, 10, 32), Open(10, 70m)),
            Held(12, "TINY", AccountThree, BucketTwo, 12, "Auto", 100_000, 0.0125m, Ist(14, 10, 33), "live", 0.012m, Ist(14, 10, 20), Open(100_000, 0.01m)),
            Held(13, "BIGL", AccountThree, BucketTwo, 13, "Auto", 3, 99_999.9999m, Ist(14, 10, 30), "quote", 99_000m, Ist(14, 10, 30), Open(3, 80_000.0001m)),
            Held(14, "RELQ", AccountTwo, BucketTwo, 1, "Energy", 7, 2850m, yesterday, "sync", 2800m, null, Open(7, 2600m)),
        };
        var pushes = new List<InstrumentPrice>
        {
            Push(1, 2915.55m, Ist(14, 10, 36)),
            Push(2, 3875.4321m, Ist(14, 10, 36, 10)),
            Push(3, 1512.9999m, Ist(14, 10, 36)),
            Push(4, 1655.5m, Ist(14, 10, 36, 1)),
            Push(5, 615m, Ist(14, 10, 36, 1)),
            Push(6, 451.0001m, Ist(14, 10, 36, 2)),
            Push(7, 101.5m, Ist(14, 10, 36, 3)),
            Push(8, 61m, Ist(14, 10, 36, 3)),
            Push(9, 106m, Ist(14, 10, 36, 30), "quote"),
            Push(11, 78.0001m, Ist(14, 10, 36, 40)),
            Push(12, 0.0126m, Ist(14, 10, 36, 40)),
            Push(13, 100_500m, Ist(14, 10, 0), "live"),
        };
        return Scenario(
            "sources-and-close-days",
            "Holdings priced by a sync, a quote and a live tick, with the previous close of the same day, of yesterday, of unknown day and none; partly and fully closed holdings; an unpriced and an unmapped one; a broker quantity that differs from the open lots'; one instrument in two accounts in different states; sub-paise prices; an older push that is ignored.",
            holdings,
            pushes);
    }

    private static GoldenScenario PushOrdering()
    {
        var holdings = new List<GoldenHolding>
        {
            Held(1, "ORDA", AccountOne, BucketOne, 1, "IT", 10, 100m, Ist(14, 10, 30), "live", 98m, Ist(14, 10, 30), Open(10, 90m)),
            Held(2, "ORDB", AccountOne, BucketOne, 2, "IT", 10, 200m, Ist(14, 10, 30), "quote", 195m, Ist(14, 10, 30), Open(10, 150m)),
            Held(3, "ORDC", AccountTwo, BucketTwo, 3, "Banks", 5, 300m, Ist(14, 10, 30), "live", 290m, Ist(14, 10, 25), Open(5, 250m)),
        };
        var pushes = new List<InstrumentPrice>
        {
            Push(1, 99m, Ist(14, 10, 29, 59)), // older than the price held: ignored
            Push(1, 100m, Ist(14, 10, 30)), // the same time and price: ignored
            Push(1, 101m, Ist(14, 10, 30)), // the same time, another price: replaces
            Push(1, 102m, Ist(14, 10, 31)),
            Push(2, 201m, Ist(14, 10, 31)),
            Push(2, 202.5m, Ist(14, 10, 32), "quote"), // a quote as the last word
            Push(3, 305m, Ist(14, 10, 33)),
            Push(3, 303m, Ist(14, 10, 32)), // delivered late: ignored once the newer one is in
        };
        return Scenario("push-ordering", "Older, equal and late pushes: only a newer price time, or the same time with another price, replaces.", holdings, pushes);
    }

    private static GoldenScenario UnpricedAndUnmapped()
    {
        var yesterday = Ist(13, 15, 30);
        var holdings = new List<GoldenHolding>
        {
            Held(1, "NOPA", AccountOne, BucketOne, 1, "Pharma", 10, 0m, null, "sync", null, null, Open(10, 100m)),
            Held(2, "NOPB", AccountOne, BucketOne, 2, "Pharma", 20, 0m, null, "sync", null, null, Open(20, 55.5m)),
            Held(3, "MIXA", AccountTwo, BucketOne, 3, "IT", 10, 500m, Ist(14, 10, 30), "quote", 495m, Ist(14, 10, 30), Open(10, 400m)),
            Held(4, "MIXB", AccountTwo, BucketOne, 4, "IT", 10, 0m, null, "sync", null, null, Open(10, 400m)),
            Held(5, "ZERC", AccountTwo, BucketTwo, 5, "Banks", 10, 50m, Ist(14, 10, 30), "quote", 0m, Ist(14, 10, 30), Open(10, 40m)),
            Held(6, "NONE", AccountTwo, BucketTwo, null, "Unclassified", 4, 12.3456m, yesterday, "sync", null, null, Open(4, 10m)),
            Held(7, "OLDX", AccountThree, BucketTwo, 7, "Metals", 0, 80m, yesterday, "sync", null, null, Closed(5, 70m, 80m)),
        };
        var pushes = new List<InstrumentPrice>
        {
            Push(2, 56.0001m, Ist(14, 10, 36)), // an unpriced holding gets its first price
            Push(3, 501m, Ist(14, 10, 36)),
            Push(5, 51m, Ist(14, 10, 36)),
            Push(7, 81m, Ist(14, 10, 36)), // a closed holding is not repriced
        };
        return Scenario("unpriced-and-unmapped", "Groups made only of unpriced holdings (no value, not zero), a mixed group, a zero previous close, an unmapped holding and a closed one.", holdings, pushes);
    }

    private static GoldenScenario NoPreviousCloseAnywhere()
    {
        var yesterday = Ist(13, 15, 30);
        var holdings = new List<GoldenHolding>
        {
            Held(1, "NCLA", AccountOne, BucketOne, 1, "Banks", 10, 100m, yesterday, "sync", null, null, Open(10, 90m)),
            Held(2, "NCLB", AccountOne, BucketTwo, 2, "IT", 30, 33.3333m, yesterday, "sync", null, null, Open(30, 30m)),
        };
        var pushes = new List<InstrumentPrice> { Push(1, 101m, Ist(14, 10, 36)), Push(2, 34m, Ist(14, 10, 36)) };
        return Scenario("no-previous-close-anywhere", "No holding has a previous close: the day change stays unavailable, every open holding is counted as excluded.", holdings, pushes);
    }
}
