using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-03: the derived baskets on a real PostgreSQL, from seeded daily bars (the network is never involved).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DerivedBasketsApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private Guid _basketId;

    /// <summary>The instruments Q000..Q119 in the order of their volume (the rank key of "Most liquid"), and the two retired ones.</summary>
    private readonly List<Guid> _ids = [];

    private Guid _retiredAfter;

    private Guid _retiredBefore;

    private static DateTimeOffset At(int month, int day) => new DateTimeOffset(2026, month, day, 16, 0, 0, Ist).ToUniversalTime();

    private static DateTimeOffset Midnight(int month, int day) => new DateTimeOffset(2026, month, day, 0, 0, 0, Ist);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedAsync();
        Factory.Clock.Pin(At(10, 23)); // Friday after the close
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private async Task SeedAsync()
    {
        _basketId = Guid.CreateVersion7();
        await Factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 120; i++)
            {
                var id = await AddInstrumentAsync(db, $"Q{i:000}", (i + 1) * 1000L, new DateOnly(2026, 10, 27), i == 119 ? "2026-10-24" : null);
                _ids.Add(id);
            }

            _retiredAfter = _ids[119]; // retired the day after the date, but with a bar on it
            _retiredBefore = await AddInstrumentAsync(db, "QOLD", 999_999_999L, new DateOnly(2026, 10, 15), "2026-10-16");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision, kind, system)
                VALUES ({_basketId}, 'Derived: Most liquid', '#64748b', 'watch', 0, now(), 0, 'derived', true)
                """);
            await db.Database.ExecuteSqlAsync($"INSERT INTO market.derived_definitions (basket_id, definition_no, enabled, cadence) VALUES ({_basketId}, 3, false, 'daily')");
            return 0;
        });
    }

    private static async Task<Guid> AddInstrumentAsync(XDrishti.Infrastructure.Persistence.XDrishtiDbContext db, string symbol, long volume, DateOnly last, string? retiredAt)
    {
        var id = Guid.CreateVersion7();
        var canonical = "NSE:" + symbol;
        var retired = retiredAt is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(retiredAt + "T00:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at, retired_at)
            VALUES ({id}, {canonical}, 'NSE', 'NSE_EQ', 'share', {canonical}, {canonical}, 1, {symbol}, false, now(), now(), {retired})
            """);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT {id}, '1D', (d::timestamp AT TIME ZONE 'Asia/Kolkata'), 100, 101, 99, 100, {volume}, 375, true
            FROM generate_series(date '2026-05-01', {last}, interval '1 day') AS d WHERE extract(dow FROM d) NOT IN (0, 6)
            """);
        return id;
    }

    private Task<HttpResponseMessage> PutParamsAsync(object body) => Client.PutJsonAsync($"/api/baskets/system/derived/{_basketId}/params", body, Ct);

    private async Task TurnOnAsync(int? topK = null)
    {
        Factory.DerivedSettings.Enabled = true;
        using var response = await PutParamsAsync(topK is { } k ? new { enabled = true, overrides = new Dictionary<string, double> { ["Basket3.TopK"] = k } } : new { enabled = true });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<DerivedRun> RefreshAsync()
    {
        using var response = await Client.PostJsonAsync("/api/baskets/system/derived/refresh", new { }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<DerivedRun>(Ct);
    }

    private static async Task<DerivedRun> ReadRunAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<DerivedRun>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), Ct))!;

    private Task<List<XDrishti.Infrastructure.Market.DerivedBaskets.BasketVersionRecord>> VersionsAsync() =>
        Factory.WithDbAsync(db => db.BasketVersions.AsNoTracking().Where(v => v.BasketId == _basketId).OrderBy(v => v.AsOf).ThenBy(v => v.CreatedAt).ToListAsync(Ct));

    private Task<List<BasketMember>> StaysAsync() => Factory.WithDbAsync(db => db.BasketMembers.AsNoTracking().Where(m => m.BasketId == _basketId).ToListAsync(Ct));

    private async Task<HashSet<Guid>> MembersAsync(string asOf)
    {
        using var response = await Client.GetAsync($"/api/baskets/{_basketId}/members?asOf={asOf}", Ct);
        return [.. (await response.ReadAsync<BasketMembers>(Ct)).Items.Select(i => i.Instrument.Id)];
    }

    [Fact]
    public async Task With_the_setting_off_refresh_is_409_and_nothing_is_written()
    {
        using var response = await Client.PostJsonAsync("/api/baskets/system/derived/refresh", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("baskets.derived_disabled");
        (await VersionsAsync()).ShouldBeEmpty();
        (await StaysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_run_stores_one_version_effective_the_next_session_with_the_retired_after_instrument_and_without_the_retired_before_one()
    {
        await TurnOnAsync();

        var run = await RefreshAsync();

        run.Baskets.Single().Outcome.ShouldBe("applied");
        var version = (await VersionsAsync()).ShouldHaveSingleItem();
        (version.AsOf, version.EffectiveFrom, version.MemberCount, version.UniverseSize, version.Provenance, version.Outcome, version.FormedAt)
            .ShouldBe((new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 26), 100, 120, "derived", "applied", null));
        version.ParamsHash.Length.ShouldBe(64);
        var stays = await StaysAsync();
        stays.Count.ShouldBe(100);
        stays.ShouldAllBe(m => m.Provenance == "derived" && m.ValidFrom == Midnight(10, 26) && m.ValidTo == null && m.SourceKind == "version" && m.SourceRef == version.Id);
        stays.Select(m => m.InstrumentId).ShouldContain(_retiredAfter); // had a bar that day: in U(D)
        var scores = await Factory.WithDbAsync(db => db.BasketMemberScores.AsNoTracking().Where(s => s.VersionId == version.Id).ToListAsync(Ct));
        scores.Count.ShouldBe(120); // the members and the next 20 by rank
        scores.Count(s => s.IsMember).ShouldBe(100);
        scores.Select(s => s.InstrumentId).ShouldNotContain(_retiredBefore); // no bar on the date: not in the universe, not even excluded
        (await MembersAsync("2026-10-26")).Count.ShouldBe(100);
        var change = (await Factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().Where(e => e.Event == AccountEventType.BasketMembershipChanged).ToListAsync(Ct))).ShouldHaveSingleItem();
        change.Detail.ShouldBe("Derived basket 'Derived: Most liquid' as of 2026-10-23: 100 added, 0 removed"); // counts only
    }

    [Fact]
    public async Task A_swap_closes_exactly_the_leavers_opens_the_newcomers_and_members_as_of_follows_the_boundary()
    {
        await TurnOnAsync(topK: 20);
        await RefreshAsync();
        var first = await MembersAsync("2026-10-26");
        var leavers = first.OrderBy(id => _ids.IndexOf(id)).Take(3).ToList(); // the lowest volumes of the 20
        var newcomers = _ids.Where(id => !first.Contains(id)).OrderBy(id => _ids.IndexOf(id)).Take(3).ToList(); // far below the buffer, now the strongest
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE market.bars_agg SET volume = 1 WHERE instrument_id = ANY({leavers.ToArray()})"));
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE market.bars_agg SET volume = 900000000 WHERE instrument_id = ANY({newcomers.ToArray()})"));
        Factory.Clock.Pin(At(10, 26));
        Owner = await Factory.SignInElevatedAsync(Ct);

        var second = await RefreshAsync();

        second.Baskets.Single().Outcome.ShouldBe("applied");
        var stays = await StaysAsync();
        stays.Count.ShouldBe(23);
        stays.Where(m => m.ValidTo != null).Select(m => m.InstrumentId).Order().ShouldBe(leavers.Order());
        stays.Where(m => m.ValidTo != null).ShouldAllBe(m => m.ValidTo == Midnight(10, 27) && m.RemovedBy != null);
        stays.Where(m => m.ValidFrom == Midnight(10, 27)).Select(m => m.InstrumentId).Order().ShouldBe(newcomers.Order());
        stays.Count(m => m.ValidFrom == Midnight(10, 26) && m.ValidTo == null).ShouldBe(17); // the other 17 rows are untouched
        (await MembersAsync("2026-10-26")).Order().ShouldBe(first.Order());
        (await MembersAsync("2026-10-27")).Order().ShouldBe(first.Except(leavers).Concat(newcomers).Order());
        var scores = await Factory.WithDbAsync(async db => await db.BasketMemberScores.AsNoTracking().Where(s => s.IsMember && s.SessionsHeld > 0).CountAsync(Ct));
        scores.ShouldBe(17); // the incumbents carried their sessions from the previous version
    }

    [Fact]
    public async Task The_same_run_twice_writes_nothing_and_a_changed_parameter_set_writes_a_new_version_beside_the_old_one()
    {
        await TurnOnAsync(topK: 20);
        await RefreshAsync();

        var again = await RefreshAsync();
        using var changed = await PutParamsAsync(new { overrides = new Dictionary<string, double> { ["Basket3.TopK"] = 10 } });
        await RefreshAsync();

        again.Baskets.Single().Outcome.ShouldBe("unchanged");
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var versions = await VersionsAsync();
        versions.Count.ShouldBe(2);
        versions.Select(v => v.ParamsHash).Distinct().Count().ShouldBe(2);
        versions[0].MemberCount.ShouldBe(20); // the older version stays readable
        versions[1].MemberCount.ShouldBe(10);
        (await StaysAsync()).Count(m => m.ValidTo == null && m.ValidFrom == Midnight(10, 26)).ShouldBe(10); // the newer one wins the same effective date
        (await MembersAsync("2026-10-26")).Count.ShouldBe(10);
        using var list = await Client.GetAsync($"/api/baskets/{_basketId}/versions", Ct);
        var page = await list.ReadAsync<BasketVersionPage>(Ct);
        page.Items.Count.ShouldBe(2);
        page.Definition.ShouldNotBeNull();
        page.Definition.Name.ShouldNotBeNullOrWhiteSpace();
        page.Definition.NotThis.ShouldStartWith("Not");
        page.Definition.Unit.ShouldBeOneOf("ratio", "percent", "rupees");
    }

    [Fact]
    public async Task A_version_lists_its_scores_named_exclusions_counts_and_parameters()
    {
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM market.bars_agg WHERE instrument_id = {_ids[0]} AND ts < '2026-09-01'")); // too little history
        await TurnOnAsync(topK: 20);
        await RefreshAsync();
        var versionId = (await VersionsAsync()).Single().Id;

        using var response = await Client.GetAsync($"/api/baskets/{_basketId}/versions/{versionId}", Ct);
        var detail = await response.ReadAsync<BasketVersionDetail>(Ct);

        detail.Excluded.ShouldHaveSingleItem().ShouldBe(new VersionExclusionView(_ids[0], "Q000", "insufficient_history"));
        (detail.Version.MemberCount + detail.BelowThreshold + detail.Excluded.Count).ShouldBe(detail.Version.UniverseSize);
        detail.Scores.Count(s => s.IsMember).ShouldBe(20);
        detail.Scores.Where(s => s.IsMember).Select(s => s.Rank).Order().ShouldBe(Enumerable.Range(1, 20));
        detail.Params["Basket3.TopK"].ShouldBe(20);
        using var missing = await Client.GetAsync($"/api/baskets/{_basketId}/versions/{Guid.NewGuid()}", Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Every_change_route_on_a_derived_basket_is_409_and_a_copy_out_is_allowed()
    {
        await TurnOnAsync(topK: 20);
        await RefreshAsync();
        var manual = await CreateBasketAsync("Mine");
        var one = (await MembersAsync("2026-10-26")).First(id => id != _retiredAfter); // a retired instrument cannot be copied into a basket

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
        (await MembersAsync("2026-10-26")).Count.ShouldBe(20);
    }

    [Fact]
    public async Task A_failure_before_the_commit_leaves_no_version_and_the_previous_membership_is_still_served()
    {
        await TurnOnAsync(topK: 20);
        await RefreshAsync();
        var now = Factory.Clock.GetUtcNow();
        // the last score belongs to no instrument: the database refuses it after the version, the member rows and the closes were staged
        var scores = new List<VersionScore> { new(_ids[5], 1, 1, true, 0), new(Guid.CreateVersion7(), 1, 2, true, 0) };
        var version = new DerivedVersionWrite(
            Guid.CreateVersion7(), _basketId, "Derived: Most liquid", new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), null, null, "{}", new string('f', 64), 2, 0, 0, "[]", BasketProvenance.Derived, VersionOutcomes.Applied, scores, now, "system:test");
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDerivedVersionStore>();

            await Should.ThrowAsync<DbUpdateException>(async () => await store.WriteAsync(version, Ct));
        }

        (await VersionsAsync()).ShouldHaveSingleItem();
        (await StaysAsync()).ShouldAllBe(m => m.ValidTo == null);
        (await MembersAsync("2026-10-26")).Count.ShouldBe(20);
        (await MembersAsync("2026-10-27")).Count.ShouldBe(20);
    }

    [Fact]
    public async Task A_backfill_writes_recomputed_versions_with_closed_windows_never_overwrites_and_stops_at_the_published_range()
    {
        await TurnOnAsync(topK: 20);
        var body = new { basketId = _basketId, from = "2026-09-14", to = "2026-09-25" };

        using var backfill = await Client.PostJsonAsync("/api/baskets/system/derived/backfill", body, Ct);
        using var again = await Client.PostJsonAsync("/api/baskets/system/derived/backfill", body, Ct);

        backfill.StatusCode.ShouldBe(HttpStatusCode.Accepted, await backfill.Content.ReadAsStringAsync(Ct));
        var run = await ReadRunAsync(backfill);
        run.Note.ShouldNotBeNull().ShouldContain("restated");
        var versions = await VersionsAsync();
        versions.Count.ShouldBe(10);
        versions.ShouldAllBe(v => v.Provenance == "recomputed" && v.MemberCount == 20);
        (await ReadRunAsync(again)).Summary.ShouldContain("0 recomputed");
        versions.Count.ShouldBe(10); // nothing overwritten, nothing added
        var stays = await StaysAsync();
        stays.ShouldAllBe(m => m.Provenance == "recomputed" && m.ValidTo != null); // closed windows: no open row from a backfill
        (await MembersAsync("2026-09-15")).Count.ShouldBe(20);

        await RefreshAsync(); // the live version, effective 2026-10-26
        using var overlap = await Client.PostJsonAsync("/api/baskets/system/derived/backfill", new { basketId = _basketId, from = "2026-10-19", to = "2026-10-23" }, Ct);
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlap.ReadProblemAsync(Ct)).Code.ShouldBe("baskets.backfill_overlap");
        (await VersionsAsync()).Count(v => v.Provenance == "derived").ShouldBe(1);
    }

    [Fact]
    public async Task Owner_acts_need_a_session_and_a_step_up_and_the_params_are_checked_and_audited()
    {
        using var anonymous = await Factory.CreateAnonymousClient().GetAsync($"/api/baskets/{_basketId}/versions", Ct);
        var plain = await Factory.SignInAsync(Ct);
        using var refresh = await plain.Client.PostJsonAsync("/api/baskets/system/derived/refresh", new { }, Ct);
        using var backfill = await plain.Client.PostJsonAsync("/api/baskets/system/derived/backfill", new { }, Ct);
        using var set = await plain.Client.PutJsonAsync($"/api/baskets/system/derived/{_basketId}/params", new { enabled = true }, Ct);
        using var unknown = await PutParamsAsync(new { overrides = new Dictionary<string, double> { ["Nope"] = 1 } });
        using var range = await PutParamsAsync(new { overrides = new Dictionary<string, double> { ["Basket3.TopK"] = -3 } });
        using var empty = await PutParamsAsync(new { });
        using var missing = await Client.PutJsonAsync($"/api/baskets/system/derived/{Guid.NewGuid()}/params", new { enabled = true }, Ct);
        using var ok = await PutParamsAsync(new { enabled = true, overrides = new Dictionary<string, double> { ["Basket3.TopK"] = 40 } });

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (refresh.StatusCode, backfill.StatusCode, set.StatusCode).ShouldBe((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
        (unknown.StatusCode, range.StatusCode, empty.StatusCode, missing.StatusCode, ok.StatusCode).ShouldBe((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.OK));
        var sets = await Factory.WithDbAsync(db => db.BasketParamSets.AsNoTracking().ToListAsync(Ct));
        sets.ShouldHaveSingleItem().ParamsHash.Length.ShouldBe(64);
        var events = await Factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().Where(e => e.Event == AccountEventType.DerivedBasketChanged).ToListAsync(Ct));
        events.ShouldHaveSingleItem().Detail.ShouldNotBeNull().ShouldContain("switched on");
    }

    [Fact]
    public async Task A_frozen_intraday_basket_can_be_switched_on_because_the_opening_minute_freeze_forms_it()
    {
        var gappers = Guid.CreateVersion7();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision, kind, system) VALUES ({gappers}, 'Derived: Gappers', '#64748b', 'watch', 0, now(), 0, 'derived', true)");
            await db.Database.ExecuteSqlAsync($"INSERT INTO market.derived_definitions (basket_id, definition_no, enabled, cadence) VALUES ({gappers}, 4, false, 'frozen_intraday')");
            return 0;
        });

        using var response = await Client.PutJsonAsync($"/api/baskets/system/derived/{gappers}/params", new { enabled = true }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Factory.WithDbAsync(db => db.DerivedDefinitions.AsNoTracking().Where(d => d.BasketId == gappers).Select(d => d.Enabled).SingleAsync(Ct))).ShouldBeTrue();
    }
}
