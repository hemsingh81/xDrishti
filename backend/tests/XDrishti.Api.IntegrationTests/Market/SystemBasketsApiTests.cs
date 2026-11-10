using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-01: the index baskets on a real PostgreSQL, with the files served from memory (the network is never reached).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SystemBasketsApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private const string File = "ind_nifty50list.csv";

    private Guid _basketId;

    private static DateTimeOffset Ist(int year, int month, int day) => new DateTimeOffset(year, month, day, 9, 30, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedAsync();
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private static string Symbol(int i) => $"T{i:000}";

    private static string Isin(int i) => $"INE{i:000}A01010";

    private async Task SeedAsync()
    {
        _basketId = Guid.CreateVersion7();
        await Factory.WithDbAsync(async db =>
        {
            for (var i = 1; i <= 60; i++)
            {
                var id = Guid.CreateVersion7();
                var canonical = "NSE:" + Symbol(i);
                var isin = Isin(i);
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at, isin)
                    VALUES ({id}, {canonical}, 'NSE', 'NSE_EQ', 'share', {canonical}, {canonical}, 1, {Symbol(i)}, false, now(), now(), {isin})
                    """);
            }

            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision, kind, system)
                VALUES ({_basketId}, 'NSE Nifty 50', '#64748b', 'watch', 0, now(), 0, 'index', true)
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO market.basket_sources (basket_id, source_url, file_name, host_alt, format_version, cadence, expected_min, expected_max, enabled, state, fail_count)
                VALUES ({_basketId}, 'https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv', {File}, NULL, 'nse-index-csv-1', 'daily', 50, 50, false, 'never', 0)
                """);
            return 0;
        });
    }

    private static string Csv(int from, int count)
    {
        var sb = new StringBuilder("Company Name,Industry,Symbol,Series,ISIN Code\r\n");
        for (var i = from; i < from + count; i++)
        {
            sb.Append($"Company {i},Misc,{Symbol(i)},EQ,{Isin(i)}\r\n");
        }

        return sb.ToString();
    }

    private async Task<FetchNowOutcome> FetchAsync(object? body = null)
    {
        using var response = await Client.PostJsonAsync("/api/baskets/system/fetch", body ?? new { basketIds = new[] { _basketId } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var run = await response.ReadAsync<BasketFetchRun>(Ct);
        return new FetchNowOutcome(run.Outcomes.Single());
    }

    private async Task<string[]> MembersAsync(string? asOf = null)
    {
        var view = await MembersViewAsync(asOf);
        return [.. view.Items.Select(i => i.Instrument.Symbol).Order(StringComparer.Ordinal)];
    }

    private async Task<BasketMembers> MembersViewAsync(string? asOf = null)
    {
        using var response = await Client.GetAsync($"/api/baskets/{_basketId}/members" + (asOf is null ? string.Empty : $"?asOf={asOf}"), Ct);
        return await response.ReadAsync<BasketMembers>(Ct);
    }

    private async Task MoveToAsync(int year, int month, int day)
    {
        Factory.Clock.Pin(Ist(year, month, day));
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private static string[] Symbols(int from, int count) => [.. Enumerable.Range(from, count).Select(i => "NSE:" + Symbol(i)).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task With_the_setting_off_fetch_now_is_409_and_no_request_is_made()
    {
        using var response = await Client.PostJsonAsync("/api/baskets/system/fetch", new { basketIds = new[] { _basketId } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("baskets.fetch_disabled");
        Factory.BasketSources.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Fetch_now_needs_a_session_and_a_step_up()
    {
        using var anonymous = await Factory.CreateAnonymousClient().PostJsonAsync("/api/baskets/system/fetch", new { }, Ct);
        var plain = await Factory.SignInAsync(Ct);
        using var withoutStepUp = await plain.Client.PostJsonAsync("/api/baskets/system/fetch", new { force = true }, Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutStepUp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutStepUp.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
        Factory.BasketSources.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_first_fetch_stores_fifty_members_from_the_india_date_with_provenance_and_the_basket_shows_its_source()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));

        var outcome = await FetchAsync();

        (outcome.View.Outcome, outcome.View.Added, outcome.View.Removed, outcome.View.Rows).ShouldBe(("applied", 50, 0, 50));
        (await MembersAsync()).ShouldBe(Symbols(1, 50));
        var rows = await Factory.WithDbAsync(db => db.BasketMembers.AsNoTracking().Where(m => m.BasketId == _basketId).ToListAsync(Ct));
        rows.ShouldAllBe(m => m.Provenance == "fetched" && m.ValidTo == null && m.SourceKind == "fetch" && m.SourceRef != null);
        rows.Select(m => m.ValidFrom).Distinct().ShouldBe([new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.FromMinutes(330))]);
        rows.ShouldAllBe(m => m.DetectedAt!.Value >= Now - Tolerance && m.DetectedAt.Value <= Now + Tolerance);

        var listed = (await ListBasketsAsync()).Single(b => b.Id == _basketId);
        (listed.Kind, listed.System, listed.MemberCount, listed.MembersKnownSince, listed.Source!.State).ShouldBe(("index", true, 50, new DateOnly(2026, 10, 11), "ok"));
        (await ListBasketsAsync()).Where(b => b.System).Select(b => b.Id).ShouldBe([_basketId]);
        using var sourceResponse = await Client.GetAsync($"/api/baskets/{_basketId}/source", Ct);
        var source = await sourceResponse.ReadAsync<BasketSourceView>(Ct);
        (source.FileName, source.Enabled, source.State, source.Fetches.Single().Outcome).ShouldBe((File, true, "ok", "applied"));
    }

    [Fact]
    public async Task The_same_file_again_is_unchanged_and_writes_no_member_row()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();

        var again = await FetchAsync();

        again.View.Outcome.ShouldBe("unchanged");
        (await Factory.WithDbAsync(db => db.BasketMembers.CountAsync(m => m.BasketId == _basketId, Ct))).ShouldBe(50);
    }

    [Fact]
    public async Task A_review_week_swap_closes_two_and_opens_two_with_the_exclusive_end_date()
    {
        Factory.BasketSources.Enabled = true;
        await MoveToAsync(2026, 9, 24);
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();
        await MoveToAsync(2026, 10, 1);
        Factory.BasketSources.Serve(File, Csv(3, 50));

        var swap = await FetchAsync();

        (swap.View.Outcome, swap.View.Added, swap.View.Removed).ShouldBe(("applied", 2, 2));
        (await MembersAsync("2026-09-30")).ShouldBe(Symbols(1, 50));
        (await MembersAsync("2026-10-01")).ShouldBe(Symbols(3, 50));
        var removed = await Factory.WithDbAsync(db => db.BasketMembers.AsNoTracking().Where(m => m.BasketId == _basketId && m.ValidTo != null).ToListAsync(Ct));
        removed.Count.ShouldBe(2);
        removed.ShouldAllBe(m => m.ValidTo == new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromMinutes(330)) && m.RemovedBy == ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task A_date_before_the_first_fetch_gets_the_first_known_members_and_the_date_they_are_known_since()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();

        var early = await MembersViewAsync("2020-01-01");

        early.Items.Count.ShouldBe(50);
        early.MembersKnownSince.ShouldBe(new DateOnly(2026, 10, 11));
        (await MembersViewAsync()).MembersKnownSince.ShouldBe(new DateOnly(2026, 10, 11));
    }

    [Fact]
    public async Task A_file_that_is_not_the_list_is_rejected_and_the_previous_membership_stays_visible()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();
        Factory.BasketSources.Serve(File, "<html>Not Found</html>", 404, "text/html");

        var outcome = await FetchAsync();

        (outcome.View.Outcome, outcome.View.Error).ShouldBe(("rejected", "http_404"));
        (await MembersAsync()).ShouldBe(Symbols(1, 50));
        using var sourceResponse = await Client.GetAsync($"/api/baskets/{_basketId}/source", Ct);
        (await sourceResponse.ReadAsync<BasketSourceView>(Ct)).State.ShouldBe("rejected");
    }

    [Fact]
    public async Task A_membership_change_is_audited_with_counts_only()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();

        var events = await MembershipEventsAsync();

        var change = events.Single(e => e.Event == AccountEventType.BasketMembershipChanged);
        change.Detail.ShouldBe("System basket 'NSE Nifty 50': 50 added, 0 removed");
        change.Detail.ShouldNotBeNull().ShouldNotContain("T001");
    }

    [Fact]
    public async Task Every_change_route_on_an_index_basket_is_409_and_a_copy_out_is_allowed()
    {
        Factory.BasketSources.Enabled = true;
        Factory.BasketSources.Serve(File, Csv(1, 50));
        await FetchAsync();
        var manual = await CreateBasketAsync("Mine");
        var members = await MembersViewAsync();
        var one = members.Items[0].Instrument.Id;

        (await SendAsync(HttpMethod.Put, $"/api/baskets/{_basketId}", new { name = "X" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{_basketId}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, $"/api/baskets/{_basketId}/members", new { instrumentIds = new[] { one } })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{_basketId}/members", new { instrumentIds = new[] { one } })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Put, $"/api/baskets/{_basketId}/members/order", new { instrumentIds = new[] { one } })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var into = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = manual.Id, toBasketId = _basketId, instrumentIds = new[] { one }, mode = "copy" });
        using var move = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = _basketId, toBasketId = manual.Id, instrumentIds = new[] { one }, mode = "move" });
        using var copy = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = _basketId, toBasketId = manual.Id, instrumentIds = new[] { one }, mode = "copy" });

        (into.StatusCode, move.StatusCode, copy.StatusCode).ShouldBe((HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.OK));
        (await ProblemAsync(into)).Code.ShouldBe("baskets.system");
        (await MembersAsync()).ShouldBe(Symbols(1, 50));

        var manualIds = (await ListBasketsAsync()).Where(b => !b.System).Select(b => b.Id).Reverse().ToArray();
        (await SendAsync(HttpMethod.Put, "/api/baskets/order", new { ids = manualIds })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Put, "/api/baskets/order", new { ids = new[] { _basketId } })).StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
    }

    private async Task<List<AuditRow>> MembershipEventsAsync() => await Factory.WithDbAsync(async db =>
        (await db.AccountEvents.AsNoTracking().Where(e => e.Event == AccountEventType.BasketMembershipChanged).ToListAsync(Ct)).Select(e => new AuditRow(e.Event, e.Detail)).ToList());

    private sealed record FetchNowOutcome(FetchOutcomeView View);

    private sealed record AuditRow(AccountEventType Event, string? Detail);
}
