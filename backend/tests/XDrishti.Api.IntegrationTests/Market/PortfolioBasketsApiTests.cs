using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>ADR 0014: portfolio buckets on the Baskets page as read-only system baskets, on a real PostgreSQL.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PortfolioBasketsApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly object PortfolioOn = new { portfolio = new { enabled = true } };

    private static readonly object PortfolioOff = new { portfolio = new { enabled = false } };

    private HttpClient Elevated { get; set; } = null!;

    private int _securityIds;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Elevated = (await Factory.SignInElevatedAsync(Ct)).Client;
    }

    private async Task<(AccountDetail Account, Guid BucketId)> AccountAsync(string name, string clientId)
    {
        var account = await Elevated.CreateAccountAsync(AccountsApi.NewAccount(name, clientId, roles: PortfolioOn), Ct);
        return (account, (await BucketsAsync()).Single(b => b.AccountId == account.Id).Id);
    }

    private async Task<List<BucketView>> BucketsAsync()
    {
        using var response = await Elevated.GetAsync("/api/portfolio-buckets", Ct);
        return await response.ReadAsync<List<BucketView>>(Ct);
    }

    /// <summary>A holding of the account in the bucket, as an import would have stored it (made-up numbers); a zero quantity is a closed one.</summary>
    private async Task HoldAsync(Guid accountId, Guid bucketId, Guid? instrumentId, string symbol, long qty)
    {
        var row = new BrokerHolding("ALL", $"9{++_securityIds:D3}", null, symbol, Math.Max(qty, 1), 100m, 110m);
        var holding = Holding.Open(accountId, bucketId, instrumentId, null, row, new DateOnly(2025, 1, 1), false, LotSource.Import, Now);
        if (qty == 0)
        {
            holding.CloseMissing(new DateOnly(2026, 10, 11), Now);
        }

        await Factory.WithDbAsync(async db =>
        {
            db.Holdings.Add(holding);
            await db.SaveChangesAsync(Ct);
            return 0;
        });
    }

    private async Task<BasketView> SystemBasketAsync(Guid bucketId) => (await ListBasketsAsync()).Single(b => b.Id == bucketId);

    private async Task<(Guid Bucket, Guid[] Instruments)> DhanWithHoldingsAsync()
    {
        await ImportAsync();
        var (account, bucket) = await AccountAsync("Dhan · Main", "1000000001");
        var instruments = new[] { (await FindAsync("NSE:TCS")).Id, (await FindAsync("NSE:RELIANCE")).Id, (await FindAsync("NSE:SBIN")).Id };
        await HoldAsync(account.Id, bucket, instruments[0], "TCS", 10);
        await HoldAsync(account.Id, bucket, instruments[1], "RELIANCE", 5);
        await HoldAsync(account.Id, bucket, instruments[2], "SBIN", 0);
        return (bucket, instruments);
    }

    [Fact]
    public async Task Portfolio_buckets_are_listed_first_as_system_baskets_by_account_then_name_and_manual_ones_keep_their_order()
    {
        var (zed, zedBucket) = await AccountAsync("Zed", "1000000002");
        var (alpha, alphaBucket) = await AccountAsync("Alpha", "1000000003");
        using var custom = await Elevated.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct);
        var growth = (await BucketsAsync()).Single(b => b.Name == "Growth");

        var baskets = await ListBasketsAsync();

        baskets.Select(b => b.Name).ShouldBe(["Alpha · Portfolio", "Zed · Portfolio", "Growth", "MyChoice", "MyLongTerm", "Best stocks"]);
        baskets.Take(3).ShouldAllBe(b => b.Kind == "portfolio" && b.System && b.ArchivedAt == null && b.Description == null && b.MemberCount == 0);
        baskets.Skip(3).ShouldAllBe(b => b.Kind == "manual" && !b.System && b.AccountId == null && b.AccountName == null);
        baskets.Take(3).Select(b => b.Position).ShouldBe([0, 1, 2]);
        baskets.Skip(3).Select(b => b.Position).ShouldBe([0, 1, 2]);
        (baskets[0].Id, baskets[0].AccountId, baskets[0].AccountName).ShouldBe((alphaBucket, alpha.Id, "Alpha"));
        (baskets[1].Id, baskets[1].AccountId, baskets[1].AccountName).ShouldBe((zedBucket, zed.Id, "Zed"));
        (baskets[2].Id, baskets[2].AccountId, baskets[2].AccountName).ShouldBe((growth.Id, null, null));
        baskets[0].Colour.ShouldBe(alpha.Color, "the account's colour");
        baskets[2].Colour.ShouldBe("#6366f1", "the neutral default for a bucket of no account");
        baskets[0].CreatedAt.ShouldBe(Now, Tolerance);
        (await ListBasketsAsync(includeArchived: true)).Select(b => b.Name).ShouldBe(baskets.Select(b => b.Name));
    }

    [Fact]
    public async Task Without_accounts_the_list_is_the_manual_baskets_with_kind_manual()
    {
        var baskets = await ListBasketsAsync();

        baskets.Select(b => (b.Name, b.Kind, b.System)).ShouldBe([("MyChoice", "manual", false), ("MyLongTerm", "manual", false), ("Best stocks", "manual", false)]);
    }

    [Fact]
    public async Task Archived_and_role_off_buckets_are_left_out_and_a_rename_of_the_account_is_followed()
    {
        var (first, firstBucket) = await AccountAsync("Dhan · Main", "1000000001");
        var (second, secondBucket) = await AccountAsync("Other", "1000000002");
        var (third, thirdBucket) = await AccountAsync("Third", "1000000003");
        (await ListBasketsAsync()).Count(b => b.System).ShouldBe(3);

        using var rolesOff = await Elevated.PutJsonAsync($"/api/accounts/{second.Id}/roles", PortfolioOff, Ct);
        using var archived = await Elevated.DeleteAsync($"/api/accounts/{third.Id}", Ct);
        using var renamed = await Elevated.PutJsonAsync($"/api/accounts/{first.Id}/connection", new { name = "Dhan · Long Term" }, Ct);

        rolesOff.StatusCode.ShouldBe(HttpStatusCode.OK);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var system = (await ListBasketsAsync()).Where(b => b.System).ToList();
        system.Select(b => (b.Id, b.Name, b.AccountName)).ShouldBe([(firstBucket, "Dhan · Long Term · Portfolio", "Dhan · Long Term")]);
        using var offMembers = await Client.GetAsync($"/api/baskets/{secondBucket}/members", Ct);
        using var archivedMembers = await Client.GetAsync($"/api/baskets/{thirdBucket}/members", Ct);
        offMembers.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        archivedMembers.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var offChange = await SendAsync(HttpMethod.Delete, $"/api/baskets/{secondBucket}");
        offChange.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Counts_and_members_are_the_instruments_of_the_current_holdings_with_quantity_and_account()
    {
        var (bucket, instruments) = await DhanWithHoldingsAsync();
        var (other, otherBucket) = await AccountAsync("Other", "1000000002");
        await HoldAsync(other.Id, otherBucket, instruments[0], "TCS", 7);

        var baskets = await ListBasketsAsync();
        var members = await MembersAsync(bucket);

        baskets.Single(b => b.Id == bucket).MemberCount.ShouldBe(2, "the closed SBIN holding does not count");
        baskets.Single(b => b.Id == otherBucket).MemberCount.ShouldBe(1);
        (members.BasketId, members.Name, members.ArchivedAt, members.AsOf, members.Total).ShouldBe((bucket, "Dhan · Main · Portfolio", null, null, 2));
        members.Items.Select(m => (m.Instrument.Symbol, m.Quantity, m.AccountName, m.Position, m.ValidTo, m.AddedBy)).ShouldBe(
        [
            ("NSE:RELIANCE", 5L, "Dhan · Main", 0, null, "portfolio"),
            ("NSE:TCS", 10L, "Dhan · Main", 1, null, "portfolio"),
        ]);
        members.Items.ShouldAllBe(m => m.Instrument.Tracked.Active == false);
    }

    [Fact]
    public async Task Holdings_without_an_instrument_of_the_master_list_are_skipped_and_the_same_instrument_in_two_accounts_of_a_custom_bucket_is_one_member()
    {
        await ImportAsync();
        var tcs = (await FindAsync("NSE:TCS")).Id;
        var (one, _) = await AccountAsync("Dhan · Main", "1000000001");
        var (two, _) = await AccountAsync("Other", "1000000002");
        using var created = await Elevated.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct);
        var growth = (await BucketsAsync()).Single(b => b.Name == "Growth").Id;
        await HoldAsync(one.Id, growth, tcs, "TCS", 10);
        await HoldAsync(two.Id, growth, tcs, "TCS", 7);
        await HoldAsync(one.Id, growth, null, "UNMAPPED", 3);

        var basket = await SystemBasketAsync(growth);
        var members = await MembersAsync(growth);

        basket.MemberCount.ShouldBe(1);
        var member = members.Items.ShouldHaveSingleItem();
        (member.Instrument.Id, member.Quantity, member.AccountName).ShouldBe((tcs, 17L, "Dhan · Main, Other"));
    }

    [Fact]
    public async Task A_bucket_without_holdings_is_an_empty_system_basket()
    {
        var (_, bucket) = await AccountAsync("Dhan · Main", "1000000001");

        (await SystemBasketAsync(bucket)).MemberCount.ShouldBe(0);
        (await MembersAsync(bucket)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_date_or_the_history_of_a_system_basket_is_a_400()
    {
        var (bucket, _) = await DhanWithHoldingsAsync();

        using var asOf = await Client.GetAsync($"/api/baskets/{bucket}/members?asOf=2026-10-10", Ct);
        using var history = await Client.GetAsync($"/api/baskets/{bucket}/history", Ct);

        asOf.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(asOf)).Code.ShouldBe("baskets.invalid");
        history.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(history)).Code.ShouldBe("baskets.invalid");
    }

    [Fact]
    public async Task Every_change_route_on_a_system_basket_is_a_409_baskets_system_and_nothing_changes()
    {
        var (bucket, instruments) = await DhanWithHoldingsAsync();
        var manual = await BasketAsync("MyChoice");
        var before = await ListBasketsAsync();
        var requests = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Put, $"/api/baskets/{bucket}", new { name = "Renamed", colour = "#112233" }),
            (HttpMethod.Delete, $"/api/baskets/{bucket}", null),
            (HttpMethod.Post, $"/api/baskets/{bucket}/members", new { instrumentIds = instruments }),
            (HttpMethod.Delete, $"/api/baskets/{bucket}/members", new { instrumentIds = instruments }),
            (HttpMethod.Put, $"/api/baskets/{bucket}/members/order", new { instrumentIds = instruments }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = manual.Id, toBasketId = bucket, instrumentIds = instruments, mode = "copy" }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = manual.Id, toBasketId = bucket, instrumentIds = instruments, mode = "move" }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = bucket, toBasketId = manual.Id, instrumentIds = instruments, mode = "move" }),
        };

        foreach (var (method, path, body) in requests)
        {
            using var response = await SendAsync(method, path, body);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{method} {path}");
            (await ProblemAsync(response)).Code.ShouldBe("baskets.system", $"{method} {path}");
        }

        (await ListBasketsAsync()).ShouldBe(before);
        (await MembersAsync(manual.Id)).Items.ShouldBeEmpty();
        (await BasketEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_manual_order_is_unaffected_and_a_system_basket_in_it_is_a_400()
    {
        var (bucket, _) = await DhanWithHoldingsAsync();
        var manual = (await ListBasketsAsync()).Where(b => !b.System).ToList();

        using var reversed = await SendAsync(HttpMethod.Put, "/api/baskets/order", new { ids = manual.AsEnumerable().Reverse().Select(b => b.Id) });
        using var withSystem = await SendAsync(HttpMethod.Put, "/api/baskets/order", new { ids = manual.Select(b => b.Id).Append(bucket) });

        reversed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListBasketsAsync()).Where(b => !b.System).Select(b => b.Name).ShouldBe(["Best stocks", "MyLongTerm", "MyChoice"]);
        withSystem.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(withSystem)).Code.ShouldBe("baskets.invalid");
        (await ListBasketsAsync()).Take(1).Single().Id.ShouldBe(bucket);
    }

    [Fact]
    public async Task Copying_from_a_system_basket_to_a_manual_one_adds_the_instruments_and_tracks_them_and_the_source_stays()
    {
        var (bucket, instruments) = await DhanWithHoldingsAsync();
        var manual = await BasketAsync("MyChoice");

        var result = await TransferAsync(bucket, manual.Id, "copy", instruments);

        result.ShouldBe(new TransferResult("copy", 3, 2, 0, 0, 1, 2, 2, 2), "SBIN is not held any more, so it is not in the source");
        (await MemberIdsAsync(manual.Id)).ShouldBe([instruments[1], instruments[0]], "the source's order (symbol)");
        (await MemberIdsAsync(bucket)).ShouldBe([instruments[1], instruments[0]]);
        (await BasketsOfAsync(instruments[0])).Select(b => b.BasketId).ShouldBe([manual.Id]);
        var tracked = await Factory.WithDbAsync(db => Task.FromResult(db.TrackedInstruments.Count(t => t.Active)));
        tracked.ShouldBe(2);
        var audit = (await BasketEventsAsync()).Single(e => e.Detail!.StartsWith("Copied", StringComparison.Ordinal));
        audit.Detail.ShouldBe("Copied 2 of 3 instruments from portfolio basket 'Dhan · Main · Portfolio' to basket 'MyChoice' (0 already there, 1 not in the source, 2 tracked now)");
        (await TransferAsync(bucket, manual.Id, "copy", instruments)).ShouldBe(new TransferResult("copy", 3, 0, 2, 0, 1, 0, 2, 2), "idempotent");
    }

    [Fact]
    public async Task The_instrument_lists_the_manual_baskets_only()
    {
        var (bucket, instruments) = await DhanWithHoldingsAsync();

        (await BasketsOfAsync(instruments[0])).ShouldBeEmpty("a system basket is not a basket the instrument was put into");
        (await SystemBasketAsync(bucket)).MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_session_the_system_basket_routes_answer_401_and_a_change_without_the_token_403()
    {
        var (bucket, _) = await DhanWithHoldingsAsync();
        var anonymous = Factory.CreateAnonymousClient();
        using var unauthorised = await anonymous.GetAsync($"/api/baskets/{bucket}/members", Ct);
        var signedIn = await Factory.SignInAsync(Ct);
        var withoutToken = Factory.CreateAnonymousClient();
        withoutToken.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        using var refused = await withoutToken.DeleteAsync($"/api/baskets/{bucket}", Ct);

        unauthorised.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
