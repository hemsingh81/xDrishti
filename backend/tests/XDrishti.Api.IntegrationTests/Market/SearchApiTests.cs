using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Search;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P1-03 acceptance 4: search, filters, pagination, facets and detail on the imported sample, on a real PostgreSQL.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SearchApiTests(ApiFactory factory) : MarketTestBase(factory)
{
    private async Task ImportAsync() => (await RefreshAsync()).Status.ShouldBe("succeeded");

    [Fact]
    public async Task Without_a_list_the_search_is_empty_not_an_error()
    {
        var page = await SearchAsync();

        page.Items.ShouldBeEmpty();
        (page.Total, page.Page, page.PageSize).ShouldBe((0, 1, 50));
        (await FacetsAsync()).Total.ShouldBe(0);
    }

    [Theory]
    [InlineData("rel", "NSE:RELIANCE")] // partial symbol, any case
    [InlineData("REL", "NSE:RELIANCE")]
    [InlineData("reliance ind", "NSE:RELIANCE")] // partial name
    [InlineData("INE002A01018", "NSE:RELIANCE")] // ISIN
    [InlineData("ine002a0101", "NSE:RELIANCE")] // partial ISIN
    [InlineData("tata consul", "NSE:TCS")]
    [InlineData("Reliance Industries", "NSE:RELIANCE")] // display name
    [InlineData("vix", "NSE:INDIA_VIX")]
    public async Task Text_matches_symbol_name_display_name_and_ISIN_anywhere_in_them(string q, string symbol)
    {
        await ImportAsync();

        var symbols = (await SearchAsync($"?q={Uri.EscapeDataString(q)}&type=share")).Items.Select(i => i.Symbol).ToList();
        if (q == "vix")
        {
            symbols = (await SearchAsync($"?q={q}")).Items.Select(i => i.Symbol).ToList();
        }

        symbols.ShouldContain(symbol);
    }

    [Fact]
    public async Task Search_text_with_wildcard_characters_matches_only_itself()
    {
        await ImportAsync();

        (await SearchAsync("?q=%25")).Items.ShouldBeEmpty();
        (await SearchAsync("?q=_")).Items.ShouldNotBeEmpty("an underscore is in NSE:INDIA_VIX and friends, matched literally");
        (await SearchAsync("?q=R_LIANCE")).Items.ShouldBeEmpty();
        (await SearchAsync($"?q={Uri.EscapeDataString("ARE&M")}")).Items.Single().Symbol.ShouldBe("NSE:ARE&M");
    }

    [Theory]
    [InlineData("type=share", 30)]
    [InlineData("type=etf", 3)]
    [InlineData("type=index", 4)]
    [InlineData("type=future", 23)]
    [InlineData("fno=true", 8)]
    [InlineData("fno=false", 52)]
    [InlineData("type=share&fno=true", 8)]
    [InlineData("sector=Information%20Technology", 2)]
    [InlineData("sector=Unclassified", 17)]
    [InlineData("type=share&sector=Unclassified", 17)]
    [InlineData("series=SM", 6)]
    [InlineData("series=EQ&type=share", 16)]
    public async Task Each_filter_narrows_the_list(string query, int expected)
    {
        await ImportAsync();

        var page = await SearchAsync($"?{query}&pageSize=100");

        page.Total.ShouldBe(expected);
        page.Items.Count.ShouldBe(expected);
    }

    [Fact]
    public async Task Filters_combine_with_each_other_and_with_the_text()
    {
        await ImportAsync();

        var financials = await SearchAsync("?type=share&fno=true&sector=Financial%20Services&sort=symbol");
        var withText = await SearchAsync("?type=share&fno=true&sector=Financial%20Services&q=bank");

        financials.Items.Select(i => i.Symbol).ShouldBe(["NSE:360ONE", "NSE:HDFCBANK", "NSE:SBIN"]);
        withText.Items.Select(i => i.Symbol).ShouldBe(["NSE:HDFCBANK", "NSE:SBIN"]); // "bank" is in HDFC BANK LTD and STATE BANK OF INDIA
    }

    [Fact]
    public async Task The_ASM_GSM_filter_splits_the_list_in_flagged_and_clear()
    {
        await ImportAsync();

        var flagged = await SearchAsync("?asmGsm=true&pageSize=100");
        var clear = await SearchAsync("?asmGsm=false&pageSize=100");

        flagged.Items.ShouldAllBe(i => i.AsmGsm);
        clear.Items.ShouldAllBe(i => !i.AsmGsm);
        (flagged.Total + clear.Total).ShouldBe(60);
        (await FacetsAsync()).AsmGsm.ShouldBe(flagged.Total);
    }

    [Fact]
    public async Task Pages_are_disjoint_complete_and_in_a_stable_order()
    {
        await ImportAsync();

        var pages = new List<InstrumentPage>();
        for (var page = 1; page <= 7; page++)
        {
            pages.Add(await SearchAsync($"?page={page}&pageSize=10"));
        }

        pages.Select(p => p.Total).Distinct().ShouldBe([60]);
        pages.Select(p => p.Items.Count).ShouldBe([10, 10, 10, 10, 10, 10, 0]);
        var all = pages.SelectMany(p => p.Items.Select(i => i.Symbol)).ToList();
        all.Distinct().Count().ShouldBe(60);
        all.ShouldBe([.. all.Order(StringComparer.Ordinal)], "ordered by canonical symbol");
        (await SearchAsync("?page=2&pageSize=10")).Items.Select(i => i.Symbol).ShouldBe(pages[1].Items.Select(i => i.Symbol));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("-symbol")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("type")]
    [InlineData("series")]
    [InlineData("sector")]
    [InlineData("-lot")]
    [InlineData("tick")]
    public async Task Every_sort_gives_the_same_complete_list_in_a_stable_order(string sort)
    {
        await ImportAsync();

        var first = await SearchAsync($"?sort={sort}&pageSize=100");
        var again = await SearchAsync($"?sort={sort}&pageSize=100");
        var paged = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            paged.AddRange((await SearchAsync($"?sort={sort}&page={page}&pageSize=25")).Items.Select(i => i.Symbol));
        }

        first.Items.Select(i => i.Symbol).ShouldBe(again.Items.Select(i => i.Symbol));
        paged.ShouldBe(first.Items.Select(i => i.Symbol));
        first.Items.Count.ShouldBe(60);
    }

    [Fact]
    public async Task Descending_and_ascending_sorts_are_opposites_and_lot_sorts_by_number()
    {
        await ImportAsync();

        var asc = (await SearchAsync("?sort=lot&pageSize=100")).Items.Select(i => i.LotSize).ToList();
        var desc = (await SearchAsync("?sort=-lot&pageSize=100")).Items.Select(i => i.LotSize).ToList();
        var symbols = (await SearchAsync("?sort=-symbol&pageSize=100")).Items.Select(i => i.Symbol).ToList();

        asc.ShouldBe([.. asc.Order()]);
        desc.ShouldBe([.. desc.OrderDescending()]);
        symbols.ShouldBe([.. symbols.OrderDescending(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Retired_instruments_are_hidden_by_default_and_shown_on_request()
    {
        await ImportAsync();
        Factory.MasterData.MasterCsv = WithoutLine(Factory.MasterData.MasterCsv, "RELIANCE-Oct2026-FUT");
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        await RefreshAsync();

        (await SearchAsync("?pageSize=100")).Total.ShouldBe(59);
        var withRetired = await SearchAsync("?pageSize=100&includeRetired=true");
        withRetired.Total.ShouldBe(60);
        withRetired.Items.Count(i => i.Retired).ShouldBe(1);
        var facets = await FacetsAsync();
        (facets.Total, facets.Retired).ShouldBe((59, 1));
    }

    [Fact]
    public async Task Facets_count_the_active_instruments_per_filter_value()
    {
        await ImportAsync();

        var facets = await FacetsAsync();

        facets.Total.ShouldBe(60);
        facets.Types.Select(t => (t.Value, t.Count)).ShouldBe([("share", 30), ("etf", 3), ("index", 4), ("future", 23)]);
        facets.FnoEligible.ShouldBe(8);
        facets.Sectors[^1].ShouldBe(new FacetCount("Unclassified", 17)); // shares only: indices, futures and ETFs never get a sector
        facets.Sectors.Where(s => s.Value != "Unclassified").Sum(s => s.Count).ShouldBe(13);
        facets.Sectors.Sum(s => s.Count).ShouldBe(facets.Types.Single(t => t.Value == "share").Count);
        facets.Sectors.Single(s => s.Value == "Financial Services").Count.ShouldBe(3);
        facets.Sectors.Take(facets.Sectors.Count - 1).Select(s => s.Value).ShouldBe([.. facets.Sectors.Take(facets.Sectors.Count - 1).Select(s => s.Value).Order(StringComparer.Ordinal)]);
        facets.Series.Single(s => s.Value == "SM").Count.ShouldBe(6);
        facets.Series.Sum(s => s.Count).ShouldBe(33, "shares and ETFs have a series, indices and futures do not");
    }

    [Fact]
    public async Task Facet_counts_match_what_the_filters_return()
    {
        await ImportAsync();
        var facets = await FacetsAsync();

        foreach (var sector in facets.Sectors)
        {
            (await SearchAsync($"?sector={Uri.EscapeDataString(sector.Value)}&pageSize=1")).Total.ShouldBe(sector.Count, sector.Value);
        }

        foreach (var type in facets.Types)
        {
            (await SearchAsync($"?type={type.Value}&pageSize=1")).Total.ShouldBe(type.Count, type.Value);
        }

        foreach (var series in facets.Series)
        {
            (await SearchAsync($"?series={series.Value}&pageSize=1")).Total.ShouldBe(series.Count, series.Value);
        }
    }

    [Fact]
    public async Task The_detail_shows_every_field()
    {
        await ImportAsync();
        var summary = (await SearchAsync("?q=360ONE-2026-10-FUT")).Items.Single();

        using var response = await Client.GetAsync($"/api/instruments/{summary.Id}", Ct);
        var detail = await response.ReadAsync<InstrumentDetail>(Ct);

        (detail.Symbol, detail.Type, detail.Name, detail.DisplayName, detail.LotSize, detail.TickSize, detail.ExpiryDate, detail.UnderlyingSymbol)
            .ShouldBe(("NSE:360ONE-2026-10-FUT", "future", "360ONE-Oct2026-FUT", "360ONE OCT FUT", 500, 10m, new DateOnly(2026, 10, 27), "360ONE"));
        (detail.ExchangeSegment, detail.Isin, detail.Series, detail.Sector, detail.FnoEligible, detail.FreezeQuantity).ShouldBe(("NSE_FNO", null, null, null, false, 20001L));
        detail.BrokerIds.ShouldHaveSingleItem().ShouldBe(new BrokerIdView("dhan", "48706", "NSE_FNO"));
        detail.FirstSeenAt.ShouldBe(Now, TimeSpan.FromMinutes(1));
        detail.LastSeenAt.ShouldBe(Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task An_unknown_instrument_is_404_with_a_stable_code()
    {
        using var response = await Client.GetAsync($"/api/instruments/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.not_found");
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("type=bond", "type")]
    [InlineData("sort=price", "sort")]
    public async Task Invalid_parameters_are_400_naming_the_field(string query, string field)
    {
        using var response = await Client.GetAsync($"/api/instruments?{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).ShouldContainKey(field);
    }

    [Fact]
    public async Task A_page_size_of_one_hundred_is_the_largest_allowed()
    {
        await ImportAsync();

        (await SearchAsync("?pageSize=100")).Items.Count.ShouldBe(60);
    }

    [Fact]
    public async Task The_trigram_indexes_can_serve_a_substring_search()
    {
        await ImportAsync();

        var plan = await Factory.WithDbAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SET enable_seqscan = off; EXPLAIN SELECT id FROM market.instruments WHERE name ILIKE '%tata%'";
            var lines = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                lines.Add(reader.GetString(0));
            }

            return string.Join('\n', lines);
        });

        plan.ShouldContain("ix_instruments_name_trgm");
    }

    [Fact]
    public async Task Search_and_facets_answer_in_well_under_200_ms_on_a_full_sized_list()
    {
        await ImportAsync();
        await AddSyntheticInstrumentsAsync(5_000);
        (await FacetsAsync()).Total.ShouldBe(5_060);
        await SearchAsync("?q=syn&pageSize=100"); // warm-up: plans, connections

        var timings = new Dictionary<string, TimeSpan>();
        foreach (var (name, query) in new[]
        {
            ("text", "?q=syn-00&pageSize=100"),
            ("name", "?q=synthetic%20company%2012&pageSize=50"),
            ("filters+sort", "?type=share&sector=Metals%20%26%20Mining&fno=false&sort=-name&page=3&pageSize=50"),
            ("isin", "?q=INE9SYN00&pageSize=100"),
            ("all", "?sort=-lot&page=40&pageSize=100"),
        })
        {
            var watch = Stopwatch.StartNew();
            var page = await SearchAsync(query);
            timings[name] = watch.Elapsed;
            page.Total.ShouldBeGreaterThan(0, name);
        }

        var facets = Stopwatch.StartNew();
        await FacetsAsync();
        timings["facets"] = facets.Elapsed;

        foreach (var (name, elapsed) in timings)
        {
            elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(200), name);
        }
    }

    private async Task AddSyntheticInstrumentsAsync(int count) =>
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, tick_size,
                    underlying_symbol, sector, sector_source, fno_eligible, first_seen_at, last_seen_at)
                SELECT gen_random_uuid(), 'NSE:SYN-' || lpad(n::text, 5, '0'), 'NSE', 'NSE_EQ', 'share', 'SYNTHETIC COMPANY ' || n, 'Synthetic Company ' || n,
                       'INE9SYN' || lpad(n::text, 5, '0'), (ARRAY['EQ','BE','SM'])[1 + n % 3], 1 + n % 50, 0.05,
                       'SYN' || n, (ARRAY['Metals & Mining','Financial Services','Healthcare', NULL])[1 + n % 4], 'nse_total_market', n % 7 = 0, now(), now()
                FROM generate_series(1, {0}) AS n
                """, count);
            await db.Database.ExecuteSqlRawAsync("ANALYZE market.instruments");
            return 0;
        });
}
