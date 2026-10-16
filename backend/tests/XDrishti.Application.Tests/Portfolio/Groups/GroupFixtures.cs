using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>
/// A small made-up portfolio for the grouping tests, valued on Monday 2026-10-12. Two accounts, three buckets, three sectors (one holding unclassified), two baskets:
/// <code>
/// AAA  one  bucket A  Banks  100 x 50 -> 60 (previous close 58)   cost 5,000  value 6,000
/// BBB  one  bucket B  Banks   10 x 200 -> 180 (previous close 190) cost 2,000  value 1,800
/// CCC  two  bucket A  IT       5 x 1000 -> 1500 (no previous close) cost 5,000  value 7,500
/// DDD  two  bucket C  (not in the master) 20 x 10, price 0 -> unpriced
/// EEE  two  bucket A  Banks   10 x 80 -> 100 (previous close 100) cost   800  value 1,000
/// baskets: Core = AAA, BBB; Dividend = AAA
/// </code>
/// </summary>
internal static class GroupFixtures
{
    public static readonly DateOnly Valuation = new(2026, 10, 12);

    public static readonly DateOnly TwoYearsAgo = new(2024, 10, 12);

    public static readonly Guid AccountOne = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    public static readonly Guid AccountTwo = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    public static readonly Guid BucketA = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    public static readonly Guid BucketB = Guid.Parse("00000000-0000-7000-8000-0000000000b2");

    public static readonly Guid BucketC = Guid.Parse("00000000-0000-7000-8000-0000000000b3");

    public static readonly Guid Core = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    public static readonly Guid DividendBasket = Guid.Parse("00000000-0000-7000-8000-0000000000c2");

    public static readonly Guid InstrumentAaa = Guid.Parse("00000000-0000-7000-8000-000000000e01");

    public static readonly Guid InstrumentBbb = Guid.Parse("00000000-0000-7000-8000-000000000e02");

    public static readonly Guid InstrumentCcc = Guid.Parse("00000000-0000-7000-8000-000000000e03");

    public static readonly Guid InstrumentEee = Guid.Parse("00000000-0000-7000-8000-000000000e05");

    public static HoldingData Held(
        string symbol, decimal price, long qty, decimal cost, DateOnly acquiredOn, Guid account, Guid bucket, Guid? instrument, decimal? previousClose = null, string status = "open") =>
        HoldingFixtures.Holding(price, symbol, status, instrument, HoldingFixtures.Open(qty, cost, acquiredOn))
            .Quoted(previousClose)
            with
        {
            AccountId = account,
            AccountName = account == AccountOne ? "Account One" : account == AccountTwo ? "Account Two" : "Account " + account.ToString("N")[..6],
            BucketId = bucket,
            BucketName = bucket == BucketA ? "Bucket A" : bucket == BucketB ? "Bucket B" : bucket == BucketC ? "Bucket C" : "Bucket " + bucket.ToString("N")[..6],
        };

    public static HoldingData Aaa() => Held("AAA", 60m, 100, 50m, TwoYearsAgo, AccountOne, BucketA, InstrumentAaa, 58m);

    public static HoldingData Bbb() => Held("BBB", 180m, 10, 200m, new DateOnly(2025, 10, 12), AccountOne, BucketB, InstrumentBbb, 190m);

    public static HoldingData Ccc() => Held("CCC", 1500m, 5, 1000m, TwoYearsAgo, AccountTwo, BucketA, InstrumentCcc);

    public static HoldingData Ddd() => Held("DDD", 0m, 20, 10m, new DateOnly(2026, 1, 1), AccountTwo, BucketC, null);

    public static HoldingData Eee() => Held("EEE", 100m, 10, 80m, new DateOnly(2025, 6, 1), AccountTwo, BucketA, InstrumentEee, 100m);

    public static HoldingData[] AllHoldings() => [Aaa(), Bbb(), Ccc(), Ddd(), Eee()];

    public static Classification Classes() => new(
        new Dictionary<Guid, string> { [InstrumentAaa] = "Banks", [InstrumentBbb] = "Banks", [InstrumentCcc] = "IT", [InstrumentEee] = "Banks" },
        new Dictionary<Guid, IReadOnlyList<BasketRef>>
        {
            [InstrumentAaa] = [new BasketRef(Core, "Core"), new BasketRef(DividendBasket, "Dividend")],
            [InstrumentBbb] = [new BasketRef(Core, "Core")],
        });

    /// <summary>The fake master and baskets to match <see cref="Classes"/>.</summary>
    public static FakeHoldingClassifications Fake()
    {
        var fake = new FakeHoldingClassifications();
        var classes = Classes();
        foreach (var (id, sector) in classes.Sectors)
        {
            fake.Sectors[id] = sector;
        }

        foreach (var (id, baskets) in classes.Baskets)
        {
            fake.Baskets[id] = [.. baskets];
        }

        return fake;
    }
}
