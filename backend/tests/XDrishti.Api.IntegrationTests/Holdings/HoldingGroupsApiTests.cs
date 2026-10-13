using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Portfolio.Groups;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The holding groups (P2-08, ADR 0021) over real holdings, lots, master sectors and baskets on TimescaleDB. Clock: Monday 2026-10-12 10:00 India time. Two accounts, three buckets, three sectors and a holding that is not in the master:
/// <code>
/// HDFCB  Example One  Core bucket   Banks    in baskets Core, Dividend   100 x 50  -> 60
/// TCS    Example One  Core bucket   IT       in basket  Core             10 x 200  -> 180
/// ICICIB Example Two  Growth        Banks    in basket  Old (archived)   5 x 1000  -> 1500
/// NOSEC  Example Two  Example Two · Portfolio  (no sector in the master)  7 x 100   -> 130
/// OTHER  Example Two  Example Two · Portfolio  (not in the master, no price) 20 x 10 -> 0
/// </code>
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingGroupsApiTests(ApiFactory factory) : HoldingsTestBase(factory)
{
    private const string TwoYearsAgo = "2024-10-12";

    private AccountDetail _one = null!;
    private AccountDetail _two = null!;
    private Guid _growth;
    private Guid _hdfc;
    private Guid _tcs;
    private Guid _icici;
    private Guid _core;
    private Guid _dividend;
    private Guid _old;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _hdfc = await Factory.SeedInstrumentAsync("HDFCB", "INE000A01011", "1001", "HDFC Bank Limited", Ct);
        _tcs = await Factory.SeedInstrumentAsync("TCS", "INE000B01012", "1002", "Tata Consultancy", Ct);
        _icici = await Factory.SeedInstrumentAsync("ICICIB", "INE000C01013", "1003", "ICICI Bank Limited", Ct);
        await Factory.SeedInstrumentAsync("NOSEC", "INE000D01014", "1004", "No Sector Limited", Ct);
        await SetSectorAsync(_hdfc, "Banks");
        await SetSectorAsync(_tcs, "IT");
        await SetSectorAsync(_icici, "Banks");

        _one = await ConnectedAccountAsync();
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange([Holding("HDFCB", 100, 50m, 60m, "1001", "INE000A01011"), Holding("TCS", 10, 200m, 180m, "1002", "INE000B01012")]);
        await ImportAllAsync(_one.Id, TwoYearsAgo);

        _two = await ConnectedAccountAsync("Example Two", "1000000002");
        Dhan.Holdings.Clear();
        Dhan.Holdings.AddRange(
        [
            Holding("ICICIB", 5, 1000m, 1500m, "1003", "INE000C01013"),
            Holding("NOSEC", 7, 100m, 130m, "1004", "INE000D01014"),
            Holding("OTHER", 20, 10m, 0m, "9999", "INE000Z01099"),
        ]);
        await ImportAllAsync(_two.Id, TwoYearsAgo);

        _growth = await AddBucketAsync("Growth");
        await ExecuteAsync("UPDATE portfolio.holdings SET bucket_id = {0} WHERE trading_symbol = 'ICICIB'", _growth);

        _core = await CreateBasketAsync("Core");
        _dividend = await CreateBasketAsync("Dividend");
        _old = await CreateBasketAsync("Old");
        await AddMembersAsync(_core, _hdfc, _tcs);
        await AddMembersAsync(_dividend, _hdfc);
        await AddMembersAsync(_old, _icici);
        using var archived = await Client.DeleteAsync($"/api/baskets/{_old}", Ct);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task ExecuteAsync(string sql, params object[] parameters) =>
        await Factory.WithDbAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, parameters, Ct));

    private Task SetSectorAsync(Guid instrumentId, string sector) =>
        ExecuteAsync("UPDATE market.instruments SET sector = {1} WHERE id = {0}", instrumentId, sector);

    private async Task<Guid> AddBucketAsync(string name)
    {
        using var response = await Client.PostJsonAsync("/api/portfolio-buckets", new { name }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BucketView>(Ct))!.Id;
    }

    private async Task<Guid> CreateBasketAsync(string name)
    {
        using var response = await Client.PostJsonAsync("/api/baskets", new { name }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BasketView>(Ct))!.Id;
    }

    private async Task AddMembersAsync(Guid basket, params Guid[] instruments)
    {
        using var response = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = instruments }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task RemoveMembersAsync(Guid basket, params Guid[] instruments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/baskets/{basket}/members", UriKind.Relative)) { Content = JsonContent.Create(new { instrumentIds = instruments }) };
        using var response = await Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<HoldingGroups> GroupsAsync(string query)
    {
        using var response = await Client.GetAsync("/api/holdings/groups" + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<HoldingGroups>(Ct);
    }

    private static HoldingGroup Group(HoldingGroups groups, string name) => groups.Groups.Single(g => g.Name == name);

    [Fact]
    public async Task Grouping_by_account_returns_the_figures_total_and_weights()
    {
        var groups = await GroupsAsync("?by=account&sort=name");

        (groups.By, groups.Additive, groups.StackBy).ShouldBe(("account", true, null));
        var one = groups.Groups[0];
        (one.Key, one.Name, one.Holdings, one.Unpriced, one.Invested, one.Value, one.Pnl, one.XirrState).ShouldBe((_one.Id.ToString(), "Example One", 2, 0, 7000m, 7800m, 800m, "ok"));
        var two = groups.Groups[1];
        (two.Holdings, two.Unpriced, two.Invested, two.Value, two.Pnl).ShouldBe((3, 1, 5700m, 8410m, 2710m)); // OTHER has no price: left out of the figures, counted as unpriced
        (groups.Total.Holdings, groups.Total.Invested, groups.Total.Value, groups.Total.WeightPct).ShouldBe((5, 12700m, 16210m, 100m));
        one.WeightPct.ShouldNotBeNull().ShouldBe(48.1184m, 0.0001m); // 7,800 of 16,210
    }

    [Fact]
    public async Task The_json_uses_the_documented_names()
    {
        using var response = await Client.GetAsync("/api/holdings/groups?by=sector&stackBy=account", Ct);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

        foreach (var name in new[] { "by", "additive", "stackBy", "groups", "total" })
        {
            json.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        var group = json.GetProperty("groups")[0];
        foreach (var name in new[] { "key", "name", "holdings", "unpriced", "invested", "value", "pnl", "pnlPct", "realised", "xirr", "xirrState", "dayChange", "dayChangePct", "dayChangeExcluded", "weightPct", "parts" })
        {
            group.TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "key", "name", "value" })
        {
            group.GetProperty("parts")[0].TryGetProperty(name, out _).ShouldBeTrue(name);
        }

        json.GetProperty("total").TryGetProperty("weightPct", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("bucket")]
    [InlineData("sector")]
    public async Task For_the_additive_groupings_the_groups_add_up_to_the_summary(string by)
    {
        var groups = await GroupsAsync($"?by={by}&status=all");
        var summary = await Client.SummaryAsync(string.Empty, Ct);

        groups.Additive.ShouldBeTrue();
        groups.Groups.Sum(g => g.Invested).ShouldBe(summary.Returns.Invested);
        groups.Groups.Sum(g => g.Value ?? 0m).ShouldBe(summary.Returns.Value!.Value);
        groups.Groups.Sum(g => g.Pnl ?? 0m).ShouldBe(summary.Returns.Pnl!.Value);
        groups.Groups.Sum(g => g.Unpriced).ShouldBe(summary.UnpricedCount);
        (groups.Total.Invested, groups.Total.Value, groups.Total.Xirr, groups.Total.XirrState).ShouldBe((summary.Returns.Invested, summary.Returns.Value, summary.Returns.Xirr, summary.Returns.XirrState));
    }

    [Fact]
    public async Task The_additive_groups_add_up_to_the_summary_of_the_account_filter_too()
    {
        var groups = await GroupsAsync($"?by=sector&accountId={_two.Id}");
        var summary = await Client.SummaryAsync($"?accountId={_two.Id}", Ct);

        groups.Groups.Sum(g => g.Invested).ShouldBe(summary.Returns.Invested);
        groups.Groups.Sum(g => g.Value ?? 0m).ShouldBe(summary.Returns.Value!.Value);
        groups.Groups.Sum(g => g.Pnl ?? 0m).ShouldBe(summary.Returns.Pnl!.Value);
    }

    [Fact]
    public async Task Buckets_include_the_custom_bucket_a_holding_was_moved_to()
    {
        var groups = await GroupsAsync("?by=bucket&sort=name");

        groups.Groups.Select(g => (g.Name, g.Holdings)).ShouldBe([("Example One · Portfolio", 2), ("Example Two · Portfolio", 2), ("Growth", 1)]);
        Group(groups, "Growth").Key.ShouldBe(_growth.ToString());
        Group(groups, "Growth").Value.ShouldBe(7500m);
    }

    [Fact]
    public async Task Sectors_come_from_the_master_and_the_rest_is_unclassified()
    {
        var groups = await GroupsAsync("?by=sector");

        groups.Groups.Select(g => (g.Key, g.Name, g.Holdings)).ToList().ShouldBe(
            [("Banks", "Banks", 2), ("~none", "Unclassified", 2), ("IT", "IT", 1)], ignoreOrder: true);
        Group(groups, "Banks").Value.ShouldBe(13500m); // HDFCB 6,000 + ICICIB 7,500
    }

    [Fact]
    public async Task Baskets_overlap_archived_ones_are_left_out_and_the_portfolio_system_baskets_are_not_counted()
    {
        var groups = await GroupsAsync("?by=basket&sort=name");

        groups.Additive.ShouldBeFalse();
        groups.Groups.Select(g => g.Name).ShouldBe(["Core", "Dividend", "No basket"]); // not "Old" (archived), not the buckets
        (Group(groups, "Core").Key, Group(groups, "Core").Holdings, Group(groups, "Core").Value).ShouldBe((_core.ToString(), 2, 7800m));
        (Group(groups, "Dividend").Holdings, Group(groups, "Dividend").Value).ShouldBe((1, 6000m)); // HDFCB is in Core and in Dividend
        (Group(groups, "No basket").Key, Group(groups, "No basket").Holdings).ShouldBe(("~none", 3)); // ICICIB's only basket is archived
        (groups.Total.Holdings, groups.Total.Value).ShouldBe((5, 16210m));
        groups.Groups.Sum(g => g.Holdings).ShouldBe(6);
    }

    [Fact]
    public async Task A_change_of_basket_membership_shows_at_once()
    {
        (Group(await GroupsAsync("?by=basket"), "Dividend").Holdings).ShouldBe(1);

        await AddMembersAsync(_dividend, _icici);
        await RemoveMembersAsync(_core, _tcs);

        var groups = await GroupsAsync("?by=basket&sort=name");
        (Group(groups, "Dividend").Holdings, Group(groups, "Core").Holdings, Group(groups, "No basket").Holdings).ShouldBe((2, 1, 3)); // ICICIB joined Dividend, TCS left Core and has no basket now
    }

    [Fact]
    public async Task Stack_by_gives_the_parts_of_each_group_that_sum_to_its_value()
    {
        var bySector = await GroupsAsync("?by=sector&stackBy=account");
        var byAccount = await GroupsAsync("?by=account&stackBy=sector&sort=name");

        bySector.StackBy.ShouldBe("account");
        var banks = Group(bySector, "Banks");
        banks.Parts!.Select(p => (p.Name, p.Value)).ShouldBe([("Example Two", 7500m), ("Example One", 6000m)]);
        bySector.Groups.ShouldAllBe(g => g.Parts!.Sum(p => p.Value) == (g.Value ?? 0m));
        byAccount.Groups[1].Parts!.Select(p => (p.Key, p.Value)).ShouldBe([("Banks", 7500m), ("~none", 910m)]);
    }

    [Fact]
    public async Task The_search_and_status_filters_apply_before_grouping()
    {
        var byText = await GroupsAsync("?by=sector&q=bank");
        var closed = await GroupsAsync("?by=sector&status=closed");

        byText.Groups.Select(g => g.Key).ShouldBe(["Banks"]); // HDFC Bank and ICICI Bank
        byText.Total.Holdings.ShouldBe(2);
        closed.Groups.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?by=account&sort=-pnl")]
    [InlineData("?by=account&sort=-name")]
    [InlineData("?by=sector&sort=xirr")]
    [InlineData("?by=basket&sort=-value")]
    public async Task Sorting_puts_groups_without_a_figure_last_and_is_stable(string query)
    {
        var first = await GroupsAsync(query);
        var second = await GroupsAsync(query);

        first.Groups.Select(g => g.Key).ShouldBe(second.Groups.Select(g => g.Key));
    }

    [Fact]
    public async Task The_holdings_list_with_a_group_returns_that_groups_holdings_with_paging_and_sort()
    {
        (await Client.ListHoldingsAsync($"?groupBy=account&groupKey={_two.Id}&sort=symbol", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ICICIB", "NOSEC", "OTHER"]);
        (await Client.ListHoldingsAsync($"?groupBy=bucket&groupKey={_growth}", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ICICIB"]);
        (await Client.ListHoldingsAsync("?groupBy=sector&groupKey=Banks&sort=-value", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ICICIB", "HDFCB"]);
        (await Client.ListHoldingsAsync("?groupBy=sector&groupKey=~none&sort=symbol", Ct)).Items.Select(i => i.Symbol).ShouldBe(["NOSEC", "OTHER"]);
        (await Client.ListHoldingsAsync($"?groupBy=basket&groupKey={_core}&sort=symbol", Ct)).Items.Select(i => i.Symbol).ShouldBe(["HDFCB", "TCS"]);
        (await Client.ListHoldingsAsync($"?groupBy=basket&groupKey={_dividend}", Ct)).Items.Select(i => i.Symbol).ShouldBe(["HDFCB"]);
        (await Client.ListHoldingsAsync("?groupBy=basket&groupKey=~none&sort=symbol", Ct)).Items.Select(i => i.Symbol).ShouldBe(["ICICIB", "NOSEC", "OTHER"]);
        var paged = await Client.ListHoldingsAsync("?groupBy=sector&groupKey=Banks&sort=symbol&pageSize=1&page=2", Ct);
        paged.Items.Select(i => i.Symbol).ShouldBe(["ICICIB"]);
        paged.Total.ShouldBe(2);
    }

    [Fact]
    public async Task The_group_key_is_url_decoded_and_an_unknown_key_gives_an_empty_page()
    {
        await SetSectorAsync(_tcs, "Oil & Gas");

        (await Client.ListHoldingsAsync("?groupBy=sector&groupKey=Oil%20%26%20Gas", Ct)).Items.Select(i => i.Symbol).ShouldBe(["TCS"]);
        var empty = await Client.ListHoldingsAsync("?groupBy=sector&groupKey=Nothing", Ct);
        (empty.Items.Count, empty.Total).ShouldBe((0, 0));
        (await Client.ListHoldingsAsync($"?groupBy=basket&groupKey={Guid.NewGuid()}", Ct)).Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_children_of_every_group_are_the_holdings_it_counts()
    {
        foreach (var by in new[] { "account", "bucket", "sector", "basket" })
        {
            var groups = await GroupsAsync($"?by={by}");
            foreach (var group in groups.Groups)
            {
                var children = await Client.ListHoldingsAsync($"?groupBy={by}&groupKey={Uri.EscapeDataString(group.Key)}&status=open", Ct);
                children.Total.ShouldBe(group.Holdings, $"{by}/{group.Name}");
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?by=strategy")]
    [InlineData("?by=account&sort=bogus")]
    [InlineData("?by=bucket&stackBy=account")]
    [InlineData("?by=sector&stackBy=sector")]
    [InlineData("?by=account&status=weird")]
    public async Task Bad_values_are_400_portfolio_invalid(string query)
    {
        using var response = await Client.GetAsync("/api/holdings/groups" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("portfolio.invalid", query);
    }

    [Theory]
    [InlineData("?groupBy=sector", "groupKey")]
    [InlineData("?groupKey=Banks", "groupBy")]
    [InlineData("?groupBy=none&groupKey=x", "groupBy")]
    public async Task A_bad_group_filter_on_the_holdings_list_is_400_portfolio_invalid(string query, string field)
    {
        using var response = await Client.GetAsync("/api/holdings" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
        var problem = await response.Content.ReadAsStringAsync(Ct);
        problem.ShouldContain("portfolio.invalid");
        problem.ShouldContain(field);
    }

    [Fact]
    public async Task An_account_that_does_not_count_in_totals_is_in_no_group_no_total_and_no_child_list_but_stays_in_the_flat_list()
    {
        using var saved = await Client.PutJsonAsync($"/api/accounts/{_two.Id}/portfolio", new { includeInTotals = false }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));

        var groups = await GroupsAsync("?by=account");
        var banks = await Client.ListHoldingsAsync("?groupBy=sector&groupKey=Banks", Ct);
        var flat = await Client.ListHoldingsAsync("?sort=symbol", Ct);
        var own = await GroupsAsync($"?by=account&accountId={_two.Id}");

        groups.Groups.Select(g => g.Name).ShouldBe(["Example One"]);
        (groups.Total.Holdings, groups.Total.Value).ShouldBe((2, 7800m));
        banks.Items.Select(i => i.Symbol).ShouldBe(["HDFCB"]);
        flat.Items.Select(i => i.Symbol).ShouldBe(["HDFCB", "ICICIB", "NOSEC", "OTHER", "TCS"]);
        own.Groups.Select(g => g.Name).ShouldBe(["Example Two"]); // an account chosen by id is shown whether or not it counts in totals
        (await Client.SummaryAsync(string.Empty, Ct)).Returns.Invested.ShouldBe(groups.Total.Invested);
    }

    [Fact]
    public async Task An_unknown_account_gives_empty_groups_with_200_like_the_holdings_list()
    {
        var groups = await GroupsAsync($"?by=sector&accountId={Guid.NewGuid()}");

        groups.Groups.ShouldBeEmpty();
        groups.Total.Holdings.ShouldBe(0);
    }

    [Theory]
    [InlineData("account", 0, 0)]
    [InlineData("sector", 1, 0)]
    [InlineData("basket", 0, 1)]
    public async Task A_request_reads_the_holdings_once_and_the_sectors_and_baskets_only_when_needed(string by, int sectorReads, int basketReads)
    {
        var reads = await StatementsDuringAsync(async () => await GroupsAsync($"?by={by}"));

        reads.Count(s => s.Contains("portfolio.holdings ", StringComparison.Ordinal)).ShouldBe(1, string.Join("\n", reads));
        reads.Count(s => s.Contains("portfolio.holding_lots", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(IsSectorRead).ShouldBe(sectorReads, string.Join("\n", reads));
        reads.Count(s => s.Contains("market.basket_members", StringComparison.Ordinal)).ShouldBe(basketReads);
    }

    [Fact]
    public async Task Stacking_sectors_by_account_reads_the_sectors_once()
    {
        var reads = await StatementsDuringAsync(async () => await GroupsAsync("?by=account&stackBy=sector"));

        reads.Count(IsSectorRead).ShouldBe(1, string.Join("\n", reads));
    }

    [Fact]
    public async Task Expanding_a_basket_group_reads_the_holdings_and_the_members_once()
    {
        var reads = await StatementsDuringAsync(async () => await Client.ListHoldingsAsync($"?groupBy=basket&groupKey={_core}", Ct));

        reads.Count(s => s.Contains("portfolio.holdings ", StringComparison.Ordinal)).ShouldBe(1);
        reads.Count(s => s.Contains("market.basket_members", StringComparison.Ordinal)).ShouldBe(1);
    }

    /// <summary>The sector read is the one statement on the master list that is not the holdings read (which joins the master list for the canonical symbol).</summary>
    private static bool IsSectorRead(string statement) =>
        statement.Contains("market.instruments", StringComparison.Ordinal) && !statement.Contains("portfolio.holdings", StringComparison.Ordinal);

    /// <summary>
    /// The text of every SQL statement the driver ran for <paramref name="request"/> and only for it: the request runs under a fresh trace of its own (sent as a W3C `traceparent` header to the in-process server), and the driver's command
    /// activities of that trace are collected — statements of background services or of other tests have another trace and are ignored.
    /// </summary>
    private async Task<List<string>> StatementsDuringAsync(Func<Task> request)
    {
        const string TestSource = "xd-test-statements";
        var statements = new List<string>();
        var trace = default(ActivityTraceId);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Npgsql" or TestSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId != trace || activity.Source.Name != "Npgsql")
                {
                    return;
                }

                var text = activity.Tags.Select(t => t.Value).FirstOrDefault(v => v is not null && v.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
                if (text is not null)
                {
                    lock (statements)
                    {
                        statements.Add(text);
                    }
                }
            },
        };
        using var source = new ActivitySource(TestSource);
        ActivitySource.AddActivityListener(listener);
        using var root = source.StartActivity("request", ActivityKind.Internal, parentContext: default).ShouldNotBeNull();
        trace = root.TraceId;

        // the in-process server starts its own trace unless the request carries one (W3C trace context): the request continues the test's
        Client.DefaultRequestHeaders.Add("traceparent", root.Id);
        try
        {
            await request();
        }
        finally
        {
            Client.DefaultRequestHeaders.Remove("traceparent");
        }

        statements.ShouldNotBeEmpty("the request's own statements were not seen: the trace did not reach the driver");
        return statements;
    }
}
