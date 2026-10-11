using XDrishti.Application.Market.Import;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class CatalogBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<MasterRow>> RowsAsync()
    {
        await using var stream = MasterFixtures.Open(MasterFixtures.Master);
        return (await DhanMasterParser.ParseAsync(stream, null, Ct)).Value.Rows;
    }

    private static async Task<SectorList> SectorsAsync()
    {
        await using var stream = MasterFixtures.Open(MasterFixtures.Sectors);
        return (await SectorList.ParseAsync(stream, Ct)).Value;
    }

    private static MasterRow Row(InstrumentType type = InstrumentType.Share, string underlying = "ABC", string name = "ABC LTD", string id = "1",
        string? isin = "INE000A01010", DateOnly? expiry = null, string instrument = "EQUITY", string segment = "E") =>
        new(type, segment, instrument, id, isin, underlying, name, name, "EQ", 1, 5m, expiry, null, null, null, null);

    [Fact]
    public async Task Canonical_symbols_follow_the_decision()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), null, Today);

        var symbols = build.Candidates.Select(c => c.CanonicalSymbol).ToHashSet();
        symbols.ShouldContain("NSE:RELIANCE");
        symbols.ShouldContain("NSE:ARE&M");
        symbols.ShouldContain("NSE:SETFNIF50");
        symbols.ShouldContain("NSE:NIFTY");
        symbols.ShouldContain("NSE:BANKNIFTY");
        symbols.ShouldContain("NSE:INDIA_VIX");
        symbols.ShouldContain("NSE:NIFTY_MIDCAP_150");
        symbols.ShouldContain("NSE:RELIANCE-2026-10-FUT");
        symbols.ShouldContain("NSE:RELIANCE-2026-12-FUT");
        symbols.ShouldContain("NSE:NIFTY-2026-11-FUT");
    }

    [Fact]
    public async Task Every_canonical_symbol_and_broker_id_is_unique_and_the_fixture_has_no_collisions()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), null, Today);

        build.Collisions.ShouldBe(0);
        build.Candidates.Select(c => c.CanonicalSymbol).Distinct().Count().ShouldBe(build.Candidates.Count);
        build.Candidates.Select(c => (c.ExchangeSegment, c.BrokerId)).Distinct().Count().ShouldBe(build.Candidates.Count);
    }

    [Fact]
    public void A_symbol_collision_keeps_the_first_row_and_is_reported_not_merged()
    {
        var rows = new[] { Row(id: "10"), Row(id: "11", name: "ABC OTHER LTD") };

        var build = CatalogBuilder.Build(rows, null, Today);

        build.Candidates.Single().BrokerId.ShouldBe("10");
        build.Collisions.ShouldBe(1);
        build.Warnings.ShouldHaveSingleItem().ShouldContain("NSE:ABC");
        build.Warnings[0].ShouldContain("11");
    }

    [Fact]
    public void One_broker_id_cannot_name_two_instruments()
    {
        var rows = new[] { Row(underlying: "AAA", id: "7"), Row(underlying: "BBB", id: "7") };

        var build = CatalogBuilder.Build(rows, null, Today);

        build.Candidates.Single().CanonicalSymbol.ShouldBe("NSE:AAA");
        build.Collisions.ShouldBe(1);
    }

    [Fact]
    public void Many_collisions_are_counted_but_only_the_first_are_listed()
    {
        var rows = Enumerable.Range(0, 25).Select(i => Row(id: i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();

        var build = CatalogBuilder.Build(rows, null, Today);

        build.Collisions.ShouldBe(24);
        build.Warnings.Count.ShouldBe(11);
        build.Warnings[^1].ShouldContain("14 more");
    }

    [Fact]
    public async Task Futures_past_their_expiry_are_dropped_but_the_expiry_day_still_counts()
    {
        var rows = await RowsAsync();

        var build = CatalogBuilder.Build(rows, null, new DateOnly(2026, 10, 11));
        var onExpiryDay = CatalogBuilder.Build(rows, null, new DateOnly(2026, 10, 27));
        var dayAfter = CatalogBuilder.Build(rows, null, new DateOnly(2026, 10, 28));

        build.Expired.ShouldBe(1);
        build.Candidates.ShouldNotContain(c => c.CanonicalSymbol == "NSE:SBIN-2026-09-FUT");
        onExpiryDay.Candidates.ShouldContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-10-FUT");
        dayAfter.Candidates.ShouldNotContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-10-FUT");
        dayAfter.Candidates.ShouldContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-11-FUT");
    }

    [Fact]
    public async Task F_and_O_eligibility_is_exactly_the_shares_with_an_active_stock_future()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), null, Today);

        var eligible = build.Candidates.Where(c => c.FnoEligible).Select(c => c.CanonicalSymbol).Order().ToArray();
        eligible.ShouldBe(["NSE:360ONE", "NSE:HDFCBANK", "NSE:INFY", "NSE:ITC", "NSE:RELIANCE", "NSE:SBIN", "NSE:TATASTEEL", "NSE:TCS"]);
        build.FnoUnderlyings.ShouldBe(8);
        build.Candidates.Where(c => c.Type != InstrumentType.Share).ShouldAllBe(c => !c.FnoEligible);
    }

    [Fact]
    public void A_share_whose_only_future_has_expired_is_not_eligible()
    {
        var rows = new[]
        {
            Row(underlying: "AAA", id: "1"),
            Row(InstrumentType.Future, "AAA", "AAA-Sep2026-FUT", "2", null, new DateOnly(2026, 9, 29), "FUTSTK", "D"),
            Row(underlying: "BBB", id: "3"),
            Row(InstrumentType.Future, "BBB", "BBB-Oct2026-FUT", "4", null, new DateOnly(2026, 10, 27), "FUTSTK", "D"),
        };

        var build = CatalogBuilder.Build(rows, null, Today);

        build.Candidates.Single(c => c.CanonicalSymbol == "NSE:AAA").FnoEligible.ShouldBeFalse();
        build.Candidates.Single(c => c.CanonicalSymbol == "NSE:BBB").FnoEligible.ShouldBeTrue();
    }

    [Fact]
    public void An_index_future_makes_nobody_eligible_and_an_orphan_stock_future_is_reported()
    {
        var rows = new[]
        {
            Row(underlying: "NIFTY", id: "1", name: "NIFTY", isin: null),
            Row(InstrumentType.Future, "NIFTY", "NIFTY-Oct2026-FUT", "2", null, new DateOnly(2026, 10, 27), "FUTIDX", "D"),
            Row(InstrumentType.Future, "GHOST", "GHOST-Oct2026-FUT", "3", null, new DateOnly(2026, 10, 27), "FUTSTK", "D"),
        };

        var build = CatalogBuilder.Build(rows, null, Today);

        build.Candidates.ShouldAllBe(c => !c.FnoEligible);
        build.Warnings.ShouldHaveSingleItem().ShouldContain("GHOST");
    }

    [Fact]
    public async Task Sectors_are_joined_by_ISIN_and_only_for_shares()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), await SectorsAsync(), Today);

        var reliance = build.Candidates.Single(c => c.CanonicalSymbol == "NSE:RELIANCE");
        reliance.Sector.ShouldBe("Oil Gas & Consumable Fuels");
        reliance.SectorSource.ShouldBe("nse_total_market");
        build.Candidates.Single(c => c.CanonicalSymbol == "NSE:TCS").Sector.ShouldBe("Information Technology");
        build.Candidates.Where(c => c.Type != InstrumentType.Share).ShouldAllBe(c => c.Sector == null && c.SectorSource == null);
        build.SectorMatched.ShouldBe(13);
        build.SectorUnmatched.ShouldBe(30 - 13);
        build.Candidates.Single(c => c.CanonicalSymbol == "NSE:FACT").Sector.ShouldBe("Chemicals");
    }

    [Fact]
    public async Task Without_a_sector_list_no_share_has_a_sector()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), null, Today);

        build.Candidates.ShouldAllBe(c => c.Sector == null);
        (build.SectorMatched, build.SectorUnmatched).ShouldBe((0, 0)); // nothing to match against
    }

    [Fact]
    public async Task The_kept_figures_add_up()
    {
        var build = CatalogBuilder.Build(await RowsAsync(), null, Today);

        build.KeptByType["share"].ShouldBe(30);
        build.KeptByType["etf"].ShouldBe(3);
        build.KeptByType["index"].ShouldBe(4);
        build.KeptByType["future"].ShouldBe(23);
        build.Candidates.Count.ShouldBe(60);
    }
}
