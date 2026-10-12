using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P1-05: track, untrack and resolve on the imported sample, on a real PostgreSQL.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class TrackingApiTests(ApiFactory factory) : MarketTestBase(factory)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    private async Task ImportAsync() => (await RefreshAsync()).Status.ShouldBe("succeeded");

    private async Task<InstrumentSummary> FindAsync(string symbol, bool includeRetired = false) =>
        (await SearchAsync($"?q={Uri.EscapeDataString(symbol)}&includeRetired={includeRetired.ToString().ToLowerInvariant()}&pageSize=100")).Items.Single(i => i.Symbol == symbol);

    private async Task<HttpResponseMessage> TrackRawAsync(params Guid[] ids) => await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct);

    private async Task<TrackResult> TrackAsync(params Guid[] ids)
    {
        using var response = await TrackRawAsync(ids);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<TrackResult>(Ct);
    }

    private async Task<HttpResponseMessage> UntrackRawAsync(object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/instruments/tracked", UriKind.Relative)) { Content = JsonContent.Create(body) };
        return await Client.SendAsync(request, Ct);
    }

    private async Task<UntrackResult> UntrackAsync(params Guid[] ids)
    {
        using var response = await UntrackRawAsync(new { instrumentIds = ids });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<UntrackResult>(Ct);
    }

    private async Task<ResolveResult> ResolveAsync(params string[] symbols)
    {
        using var response = await Client.PostJsonAsync("/api/instruments/tracked/resolve", new { symbols }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<ResolveResult>(Ct);
    }

    private Task<List<AccountEvent>> TrackingEventsAsync() =>
        Factory.WithDbAsync(db => db.AccountEvents.AsNoTracking()
            .Where(e => e.Event == AccountEventType.InstrumentsTracked || e.Event == AccountEventType.InstrumentsUntracked)
            .OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(Ct));

    [Fact]
    public async Task Every_instrument_starts_with_a_tracked_block_that_says_not_tracked()
    {
        await ImportAsync();

        var page = await SearchAsync("?pageSize=100");

        page.Items.ShouldAllBe(i => i.Tracked != null && !i.Tracked.Active && !i.Tracked.RetiredButTracked && i.Tracked.Since == null);
        (await FacetsAsync()).ShouldSatisfyAllConditions(f => f.Tracked.ShouldBe(0), f => f.TrackedRetired.ShouldBe(0));
    }

    [Fact]
    public async Task Tracking_shows_in_the_list_the_detail_the_filters_and_the_counts_and_is_audited_with_counts()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");

        var result = await TrackAsync(tcs.Id, reliance.Id, tcs.Id);

        result.ShouldBe(new TrackResult(2, 2, 0, 0, 2));
        var listed = await FindAsync("NSE:TCS");
        listed.Tracked.Active.ShouldBeTrue();
        listed.Tracked.Since.ShouldNotBeNull().ShouldBe(Now, Tolerance); // the test clock runs on from the pinned instant
        listed.Tracked.BackfillRequestedAt.ShouldBe(listed.Tracked.Since);
        (listed.Tracked.DataFrom, listed.Tracked.DataTo, listed.Tracked.LastUpdate, listed.Tracked.RetiredButTracked).ShouldBe((null, null, null, false));
        using var detail = await Client.GetAsync($"/api/instruments/{tcs.Id}", Ct);
        (await detail.ReadAsync<InstrumentDetail>(Ct)).Tracked.ShouldBe(listed.Tracked);

        var tracked = await SearchAsync("?tracked=true");
        var untracked = await SearchAsync("?tracked=false&pageSize=100");
        tracked.Items.Select(i => i.Symbol).Order().ShouldBe(["NSE:RELIANCE", "NSE:TCS"]);
        tracked.Total.ShouldBe(2);
        untracked.Total.ShouldBe(58);
        untracked.Items.ShouldNotContain(i => i.Id == tcs.Id);
        (await SearchAsync("?pageSize=100")).Total.ShouldBe(60, "no filter lists tracked and untracked together");
        (await FacetsAsync()).Tracked.ShouldBe(2);

        var audit = (await TrackingEventsAsync()).ShouldHaveSingleItem();
        (audit.Event, audit.AccountId, audit.Actor).ShouldBe((AccountEventType.InstrumentsTracked, null, ApiFactory.OwnerEmail));
        audit.Detail.ShouldBe("Tracked 2 of 2 instruments (2 new, 0 tracked again, 0 already tracked)");
    }

    [Fact]
    public async Task The_tracked_filter_combines_with_the_other_filters_and_the_text()
    {
        await ImportAsync();
        var shares = (await SearchAsync("?type=share&pageSize=100")).Items.Take(5).ToList();
        var future = (await SearchAsync("?type=future&pageSize=100")).Items[0];
        await TrackAsync([.. shares.Select(s => s.Id), future.Id]);

        (await SearchAsync("?tracked=true&type=future")).Items.Select(i => i.Id).ShouldBe([future.Id]);
        (await SearchAsync("?tracked=true&type=share")).Total.ShouldBe(5);
        var byText = await SearchAsync($"?tracked=true&q={Uri.EscapeDataString(shares[0].Symbol)}"); // a substring: may match more, but only tracked ones
        byText.Items.ShouldContain(i => i.Id == shares[0].Id);
        byText.Items.ShouldAllBe(i => i.Tracked.Active);
        (await SearchAsync("?tracked=false&type=share&pageSize=100")).Total.ShouldBe(25);
        (await SearchAsync("?tracked=true&type=index")).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Tracking_twice_changes_nothing_and_adds_no_second_audit_entry()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);
        var since = (await FindAsync("NSE:TCS")).Tracked.Since;
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var again = await TrackAsync(tcs.Id);

        again.ShouldBe(new TrackResult(1, 0, 0, 1, 1));
        (await FindAsync("NSE:TCS")).Tracked.Since.ShouldBe(since);
        (await TrackingEventsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Untracking_keeps_the_history_and_tracking_again_reactivates_the_same_row()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);
        var since = (await FindAsync("NSE:TCS")).Tracked.Since;
        // The history service (P1-12) will fill these; until then a test stands in for it.
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE market.tracked_instruments SET data_from = '2020-01-01', data_to = '2026-10-09', last_update = '2026-10-10T10:00:00Z'", Ct));
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));

        var untracked = await UntrackAsync(tcs.Id);

        untracked.ShouldBe(new UntrackResult(1, 1, 0, 0));
        var off = (await FindAsync("NSE:TCS")).Tracked;
        (off.Active, off.DataFrom, off.DataTo, off.Since).ShouldBe((false, new DateOnly(2020, 1, 1), new DateOnly(2026, 10, 9), since));
        off.DeactivatedAt.ShouldNotBeNull().ShouldBe(Now.AddMinutes(10), Tolerance);
        (await SearchAsync("?tracked=true")).Items.ShouldBeEmpty();
        (await SearchAsync("?tracked=false&q=NSE:TCS")).Items.ShouldContain(i => i.Id == tcs.Id);
        (await FacetsAsync()).Tracked.ShouldBe(0);

        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var again = await TrackAsync(tcs.Id);

        again.ShouldBe(new TrackResult(1, 0, 1, 0, 1));
        var on = (await FindAsync("NSE:TCS")).Tracked;
        (on.Active, on.DeactivatedAt, on.DataFrom, on.DataTo, on.LastUpdate).ShouldBe((true, null, new DateOnly(2020, 1, 1), new DateOnly(2026, 10, 9), new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero)));
        on.Since.ShouldNotBeNull().ShouldBe(Now.AddMinutes(20), Tolerance);
        on.Since!.Value.ShouldBeGreaterThan(since!.Value);
        on.BackfillRequestedAt.ShouldBe(on.Since);
        (await Factory.WithDbAsync(db => db.TrackedInstruments.CountAsync(Ct))).ShouldBe(1);
        (await TrackingEventsAsync()).Select(e => e.Event).ShouldBe([AccountEventType.InstrumentsTracked, AccountEventType.InstrumentsUntracked, AccountEventType.InstrumentsTracked]);
    }

    [Fact]
    public async Task Untracking_is_idempotent_counts_and_audits_only_real_changes()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var sbin = await FindAsync("NSE:SBIN");
        await TrackAsync(tcs.Id);

        var first = await UntrackAsync(tcs.Id, sbin.Id);
        var second = await UntrackAsync(tcs.Id, sbin.Id);

        first.ShouldBe(new UntrackResult(2, 1, 1, 0));
        second.ShouldBe(new UntrackResult(2, 0, 2, 0));
        var events = await TrackingEventsAsync();
        events.Count.ShouldBe(2);
        events[1].Detail.ShouldBe("Untracked 1 of 2 instruments (1 were not tracked)");
    }

    [Fact]
    public async Task A_retired_instrument_cannot_be_newly_tracked_and_nothing_in_the_call_is_tracked()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var future = await FindAsync("NSE:RELIANCE-2026-10-FUT");
        await RetireFutureAsync();

        using var response = await TrackRawAsync(tcs.Id, future.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("instruments.retired");
        problem.RootElement.GetProperty("errors").GetProperty("instrumentIds")[0].GetString()!.ShouldContain("NSE:RELIANCE-2026-10-FUT");
        (await SearchAsync("?tracked=true")).Items.ShouldBeEmpty();
        (await TrackingEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_instrument_that_retires_after_being_tracked_stays_tracked_with_a_warning_in_the_list_and_the_counts()
    {
        await ImportAsync();
        var future = await FindAsync("NSE:RELIANCE-2026-10-FUT");
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(future.Id, tcs.Id);
        await RetireFutureAsync();

        var tracked = await SearchAsync("?tracked=true");

        tracked.Items.Select(i => i.Symbol).Order().ShouldBe(["NSE:RELIANCE-2026-10-FUT", "NSE:TCS"]); // retired-but-tracked appears without includeRetired
        var warned = tracked.Items.Single(i => i.Id == future.Id);
        (warned.Retired, warned.Tracked.Active, warned.Tracked.RetiredButTracked).ShouldBe((true, true, true));
        tracked.Items.Single(i => i.Id == tcs.Id).Tracked.RetiredButTracked.ShouldBeFalse();
        using var detail = await Client.GetAsync($"/api/instruments/{future.Id}", Ct);
        (await detail.ReadAsync<InstrumentDetail>(Ct)).Tracked.RetiredButTracked.ShouldBeTrue();
        var facets = await FacetsAsync();
        (facets.Tracked, facets.TrackedRetired).ShouldBe((2, 1));
        (await SearchAsync("?tracked=false&includeRetired=true&pageSize=100")).Items.ShouldNotContain(i => i.Id == future.Id);
        (await SearchAsync("?tracked=false&pageSize=100")).Items.ShouldNotContain(i => i.Id == future.Id);

        // Tracking it again changes nothing and is not refused; untracking it is allowed, after which the warning is gone and it cannot come back.
        (await TrackAsync(future.Id)).ShouldBe(new TrackResult(1, 0, 0, 1, 2));
        (await UntrackAsync(future.Id)).Untracked.ShouldBe(1);
        (await FindAsync("NSE:RELIANCE-2026-10-FUT", includeRetired: true)).Tracked.RetiredButTracked.ShouldBeFalse();
        using var refused = await TrackRawAsync(future.Id);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_ids_and_bad_lists_are_rejected_with_400_and_change_nothing()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");

        using var unknown = await TrackRawAsync(tcs.Id, Guid.NewGuid());
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.invalid");

        foreach (var body in new object?[] { new { instrumentIds = Array.Empty<Guid>() }, new { }, new { instrumentIds = new[] { Guid.Empty } }, new { instrumentIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() } })
        {
            using var response = await Client.PostJsonAsync("/api/instruments/tracked", body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.invalid");
            using var delete = await UntrackRawAsync(body);
            delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var noBody = await Client.PostAsync("/api/instruments/tracked", Ct);
        noBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SearchAsync("?tracked=true")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Five_hundred_ids_in_one_call_are_accepted_and_all_or_nothing()
    {
        await ImportAsync();
        var ids = (await SearchAsync("?pageSize=60")).Items.Select(i => i.Id).ToList();
        ids.Count.ShouldBe(60);

        (await TrackAsync([.. ids])).ShouldBe(new TrackResult(60, 60, 0, 0, 60));

        (await UntrackAsync([.. ids])).ShouldBe(new UntrackResult(60, 60, 0, 0));
    }

    [Fact]
    public async Task Concurrent_calls_tracking_the_same_instruments_both_succeed_without_duplicates()
    {
        await ImportAsync();
        var ids = (await SearchAsync("?type=share&pageSize=30")).Items.Select(i => i.Id).ToArray();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => TrackAsync(ids)));

        results.Sum(r => r.Added).ShouldBe(30);
        results.ShouldAllBe(r => r.Added + r.AlreadyTracked == 30);
        (await Factory.WithDbAsync(db => db.TrackedInstruments.CountAsync(Ct))).ShouldBe(30);
        (await FacetsAsync()).Tracked.ShouldBe(30);
    }

    [Fact]
    public async Task Resolve_matches_tickers_canonical_symbols_and_ISINs_and_lists_the_rest_unmatched()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);

        var result = await ResolveAsync(" reliance ", "nse:tcs", "INE467B01029", "ine002a01018", "NSE:RELIANCE-2026-10-FUT", "NOSUCH", "RELIAN", "tcs", "");

        result.Matched.Select(m => m.Symbol).ShouldBe(["NSE:RELIANCE", "NSE:TCS", "NSE:RELIANCE-2026-10-FUT"]);
        result.Matched[0].Term.ShouldBe("reliance");
        result.Matched.Single(m => m.Symbol == "NSE:TCS").ShouldSatisfyAllConditions(m => m.Tracked.ShouldBeTrue(), m => m.Isin.ShouldBe("INE467B01029"), m => m.Type.ShouldBe("share"));
        result.Matched.Single(m => m.Symbol == "NSE:RELIANCE").Tracked.ShouldBeFalse();
        result.Unmatched.Select(u => (u.Term, u.Reason)).ShouldBe([("NOSUCH", "not_found"), ("RELIAN", "not_found")]);
        (result.Entries, result.Duplicates).ShouldBe((8, 3)); // the ISIN of TCS, the ISIN of RELIANCE and "tcs" repeat instruments already matched
    }

    [Fact]
    public async Task Resolve_flags_a_retired_instrument_and_the_ids_it_returns_can_be_tracked()
    {
        await ImportAsync();
        await RetireFutureAsync();

        var result = await ResolveAsync("NSE:RELIANCE-2026-10-FUT", "SBIN");

        result.Matched.Single(m => m.Symbol == "NSE:RELIANCE-2026-10-FUT").Retired.ShouldBeTrue();
        var sbin = result.Matched.Single(m => m.Symbol == "NSE:SBIN");
        (await TrackAsync(sbin.Id)).Added.ShouldBe(1);
    }

    [Fact]
    public async Task Resolve_does_not_match_text_inside_a_symbol_a_name_or_a_wildcard()
    {
        await ImportAsync();

        var result = await ResolveAsync("%", "_", "RELIANCE INDUSTRIES LTD", "TATA", "NSE:%");

        result.Matched.ShouldBeEmpty();
        result.Unmatched.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Resolve_takes_five_thousand_entries_but_not_more_and_empty_is_rejected()
    {
        await ImportAsync();

        var big = await ResolveAsync([.. Enumerable.Range(0, 5000).Select(i => $"X{i}")]);
        big.Unmatched.Count.ShouldBe(5000);

        using var tooMany = await Client.PostJsonAsync("/api/instruments/tracked/resolve", new { symbols = Enumerable.Range(0, 5001).Select(i => $"X{i}").ToArray() }, Ct);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.invalid");
        using var empty = await Client.PostJsonAsync("/api/instruments/tracked/resolve", new { symbols = Array.Empty<string>() }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var missing = await Client.PostJsonAsync("/api/instruments/tracked/resolve", new { }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_tracked_route_does_not_collide_with_the_instrument_by_id_route()
    {
        await ImportAsync();

        using var get = await Client.GetAsync("/api/instruments/tracked", Ct);
        using var byId = await Client.GetAsync($"/api/instruments/{Guid.NewGuid()}", Ct);

        get.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        byId.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_refresh_keeps_tracked_instruments_and_their_tracking()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        await RefreshAsync(force: true);

        var after = await FindAsync("NSE:TCS");
        after.Id.ShouldBe(tcs.Id);
        after.Tracked.Active.ShouldBeTrue();
    }

    [Fact]
    public async Task Tracking_is_listed_in_the_contract()
    {
        using var contract = await Client.GetAsync("/api/openapi/v1.json", Ct);
        var json = await contract.Content.ReadAsStringAsync(Ct);

        foreach (var operation in new[] { "TrackInstruments", "UntrackInstruments", "ResolveInstruments" })
        {
            json.ShouldContain($"\"operationId\": \"{operation}\"");
        }
    }

    private async Task RetireFutureAsync()
    {
        Factory.MasterData.MasterCsv = WithoutLine(Factory.MasterData.MasterCsv, "RELIANCE-Oct2026-FUT");
        Factory.MasterData.MasterETag = "\"dhan-retired\"";
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        (await RefreshAsync()).Retired.ShouldBe(1);
    }
}
