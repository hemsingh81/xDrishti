using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Refresh;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P1-03 acceptance 1 and 2: the refresh against stub file servers and a real PostgreSQL (the committed samples of the real files).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RefreshApiTests(ApiFactory factory) : MarketTestBase(factory)
{
    [Fact]
    public async Task Starting_a_refresh_answers_202_and_the_status_shows_it_running_until_the_worker_has_done_it()
    {
        var started = await StartRefreshAsync();

        var running = await GetStatusAsync();
        running.Current.ShouldNotBeNull();
        running.Current.Id.ShouldBe(started.RefreshId);
        (running.Current.Status, running.Current.Phase, running.Current.Trigger, running.Current.RequestedBy).ShouldBe(("running", "downloading", "manual", FakeOwnerEmail));
        running.Last.ShouldBeNull();
        Factory.MasterData.Requests.ShouldBeEmpty("nothing is downloaded before the worker runs the job");

        await Factory.RefreshJobs.RunPendingAsync(Factory.Services, Ct);

        var done = await GetStatusAsync();
        done.Current.ShouldBeNull();
        done.Last!.Id.ShouldBe(started.RefreshId);
        done.Last.Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task A_refresh_imports_the_in_scope_instruments_and_reports_what_it_did()
    {
        var run = await RefreshAsync();

        run.Status.ShouldBe("succeeded");
        run.Phase.ShouldBeNull();
        run.FinishedAt.ShouldNotBeNull();
        (run.Added, run.Updated, run.Retired, run.Revived, run.Unchanged).ShouldBe((60, 0, 0, 0, 0));
        run.TotalActive.ShouldBe(60);
        run.RowsRead.ShouldBe(75);
        run.SkippedByScope.ShouldBe(15);
        run.Collisions.ShouldBe(0);
        run.KeptByType["share"].ShouldBe(30);
        run.KeptByType["etf"].ShouldBe(3);
        run.KeptByType["index"].ShouldBe(4);
        run.KeptByType["future"].ShouldBe(23);
        run.FnoUnderlyings.ShouldBe(8);
        (run.SectorMatched, run.SectorUnmatched).ShouldBe((13, 17));
        run.Warnings.ShouldBeEmpty();
        run.Error.ShouldBeNull();

        var status = await GetStatusAsync();
        status.TotalActive.ShouldBe(60);
        (status.AutoRefresh.Enabled, status.AutoRefresh.TimeIst).ShouldBe((false, "08:45")); // switched off for the tests, default time
    }

    [Fact]
    public async Task The_stored_list_has_canonical_symbols_flags_sectors_and_broker_ids()
    {
        await RefreshAsync();

        var reliance = (await SearchAsync("?q=NSE:RELIANCE&type=share")).Items.Single();
        reliance.Symbol.ShouldBe("NSE:RELIANCE");
        (reliance.Name, reliance.Type, reliance.Series, reliance.Sector, reliance.LotSize, reliance.TickSize, reliance.FnoEligible, reliance.Isin)
            .ShouldBe(("RELIANCE INDUSTRIES LTD", "share", "EQ", "Oil Gas & Consumable Fuels", 1, 10m, true, "INE002A01018"));

        using var response = await Client.GetAsync($"/api/instruments/{reliance.Id}", Ct);
        var detail = await response.ReadAsync<XDrishti.Application.Market.Search.InstrumentDetail>(Ct);
        detail.BrokerIds.ShouldHaveSingleItem().ShouldBe(new XDrishti.Application.Market.Search.BrokerIdView("dhan", "2885", "NSE_EQ"));
        (detail.Exchange, detail.ExchangeSegment, detail.UnderlyingSymbol, detail.SectorSource).ShouldBe(("NSE", "NSE_EQ", "RELIANCE", "nse_total_market"));
        detail.FirstSeenAt.ShouldBe(Now, TimeSpan.FromMinutes(1));
        detail.RetiredAt.ShouldBeNull();
    }

    [Fact]
    public async Task Futures_indices_and_the_expired_future_are_handled_by_the_scope_rules()
    {
        await RefreshAsync();

        var symbols = (await SearchAsync("?pageSize=100&includeRetired=true")).Items.Select(i => i.Symbol).ToHashSet();
        symbols.ShouldContain("NSE:RELIANCE-2026-10-FUT");
        symbols.ShouldContain("NSE:NIFTY-2026-11-FUT");
        symbols.ShouldContain("NSE:NIFTY_MIDCAP_150");
        symbols.ShouldContain("NSE:INDIA_VIX");
        symbols.ShouldNotContain("NSE:SBIN-2026-09-FUT"); // already expired
        symbols.ShouldNotContain(s => s.Contains("NSETEST", StringComparison.Ordinal));
        symbols.ShouldNotContain("NSE:656MH32"); // debt
        (await SearchAsync("?q=RELIANCE-2026-10")).Items.Single().ExpiryDate.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public async Task Re_running_with_an_unchanged_file_reports_unchanged_and_does_no_work()
    {
        await RefreshAsync();
        var seenBefore = await LastSeenAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var second = await RefreshAsync();

        second.Status.ShouldBe("unchanged");
        (second.Added, second.Updated, second.Retired, second.Revived, second.Unchanged).ShouldBe((0, 0, 0, 0, 0));
        second.TotalActive.ShouldBe(60);
        Factory.MasterData.MasterRequests.Count().ShouldBe(2);
        Factory.MasterData.MasterRequests.Last().Header("If-None-Match").ShouldBe("\"dhan-v1\"");
        Factory.MasterData.SectorRequests.Count().ShouldBe(1, "an unchanged master needs no sector list");
        (await LastSeenAsync()).ShouldBe(seenBefore, "no row was touched");
    }

    [Fact]
    public async Task An_unchanged_run_keeps_the_validators_so_the_next_run_is_still_conditional()
    {
        await RefreshAsync();
        await RefreshAsync();

        await RefreshAsync();

        Factory.MasterData.MasterRequests.Select(r => r.Header("If-None-Match")).ShouldBe(["", "\"dhan-v1\"", "\"dhan-v1\""]);
    }

    [Fact]
    public async Task A_forced_refresh_downloads_again_and_reports_nothing_added_or_retired_when_nothing_changed()
    {
        await RefreshAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var forced = await RefreshAsync(force: true);

        forced.Trigger.ShouldBe("forced");
        forced.Status.ShouldBe("succeeded");
        (forced.Added, forced.Updated, forced.Retired, forced.Revived, forced.Unchanged).ShouldBe((0, 0, 0, 0, 60));
        forced.TotalActive.ShouldBe(60);
        Factory.MasterData.MasterRequests.Last().Header("If-None-Match").ShouldBeEmpty();
        Factory.MasterData.SectorRequests.Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_changed_file_reports_added_updated_retired_and_revived()
    {
        await RefreshAsync();
        var original = Factory.MasterData.MasterCsv;
        var withoutFuture = WithoutLine(original, "RELIANCE-Oct2026-FUT");
        var changedLot = WithLine(withoutFuture, "TATA CONSULTANCY SERV LT", line => line.Replace(",ES,EQ,1.0,", ",ES,EQ,5.0,", StringComparison.Ordinal));
        var withNew = changedLot + "\n" + ShareRow(id: "900001", ticker: "NEWCO", name: "NEW CO LTD", isin: "INE999Z01010");
        Factory.MasterData.MasterCsv = withNew;
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var second = await RefreshAsync();

        second.Status.ShouldBe("succeeded");
        (second.Added, second.Updated, second.Retired, second.Revived, second.Unchanged).ShouldBe((1, 1, 1, 0, 58));
        second.TotalActive.ShouldBe(60);
        (await SearchAsync("?q=NSE:TCS&type=share")).Items.Single().LotSize.ShouldBe(5);
        (await SearchAsync("?q=NEWCO")).Items.Single().Name.ShouldBe("NEW CO LTD");
        (await SearchAsync("?q=RELIANCE-2026-10")).Items.ShouldBeEmpty("a retired instrument is hidden by default");
        var retired = (await SearchAsync("?q=RELIANCE-2026-10&includeRetired=true")).Items.Single();
        retired.Retired.ShouldBeTrue();

        // The retired future comes back, the new share leaves, the lot size returns.
        Factory.MasterData.MasterCsv = original;
        Factory.MasterData.MasterETag = "\"dhan-v3\"";
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        var third = await RefreshAsync();

        (third.Added, third.Updated, third.Retired, third.Revived, third.Unchanged).ShouldBe((0, 1, 1, 1, 58));
        third.TotalActive.ShouldBe(60);
        (await SearchAsync("?q=RELIANCE-2026-10")).Items.Single().Retired.ShouldBeFalse();
        (await SearchAsync("?q=NEWCO")).Items.ShouldBeEmpty();
        (await SearchAsync("?q=NEWCO&includeRetired=true")).Items.Single().Retired.ShouldBeTrue();
        (await SearchAsync("?q=NSE:TCS&type=share")).Items.Single().LotSize.ShouldBe(1);
    }

    [Fact]
    public async Task A_future_that_passes_its_expiry_is_retired_by_the_next_refresh()
    {
        await RefreshAsync();
        // 2026-10-27 19:00 UTC is 2026-10-28 in India: the October contracts are over.
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero));
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        await ResignInAsync();

        var run = await RefreshAsync();

        run.Retired.ShouldBe(9, "the eight October stock futures and the October NIFTY future");
        (await SearchAsync("?type=future&pageSize=100")).Total.ShouldBe(23 - 9);
    }

    [Fact]
    public async Task A_failed_download_leaves_the_previous_list_untouched_and_is_recorded()
    {
        await RefreshAsync();
        var before = await SearchAsync("?pageSize=100");
        Factory.MasterData.MasterOverride = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var failed = await RefreshAsync(force: true);

        failed.Status.ShouldBe("failed");
        failed.Error.ShouldBe("Dhan's instrument file answered HTTP 503.");
        failed.FinishedAt.ShouldNotBeNull();
        (await SearchAsync("?pageSize=100")).Items.ShouldBe(before.Items);
        (await GetStatusAsync()).TotalActive.ShouldBe(60);

        // The next try works again, and the failure did not spoil the validators.
        Factory.MasterData.MasterOverride = null;
        (await RefreshAsync()).Status.ShouldBe("unchanged");
    }

    [Fact]
    public async Task A_file_with_a_changed_header_is_refused_with_a_clear_message_and_the_list_stays()
    {
        await RefreshAsync();
        Factory.MasterData.MasterCsv = Factory.MasterData.MasterCsv.Replace("TICK_SIZE", "TICK", StringComparison.Ordinal);
        Factory.MasterData.MasterETag = "\"dhan-v2\"";

        var failed = await RefreshAsync();

        failed.Status.ShouldBe("failed");
        failed.Error!.ShouldContain("changed shape");
        failed.Error!.ShouldContain("TICK_SIZE");
        (await GetStatusAsync()).TotalActive.ShouldBe(60);
        (await SearchAsync("?q=NSE:TCS")).Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_failure_while_saving_rolls_everything_back()
    {
        await RefreshAsync();
        Factory.MasterData.MasterCsv = WithoutLine(Factory.MasterData.MasterCsv, "RELIANCE-Oct2026-FUT")
            + "\n" + ShareRow(id: "900001", ticker: "NEWCO", name: "NEW CO LTD", isin: "INE999Z01010");
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        // The database refuses to update TCS: the new share is inserted first, so only a real rollback leaves no trace of it.
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION market.refuse_tcs() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                    IF NEW.canonical_symbol = 'NSE:TCS' THEN RAISE EXCEPTION 'refused by the test'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER refuse_tcs BEFORE UPDATE ON market.instruments FOR EACH ROW EXECUTE FUNCTION market.refuse_tcs();
                """);
            return 0;
        });
        try
        {
            var failed = await RefreshAsync();

            failed.Status.ShouldBe("failed");
            failed.Error!.ShouldContain("list was not changed");
            failed.Error!.ShouldNotContain("refused by the test");
        }
        finally
        {
            await Factory.WithDbAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_tcs ON market.instruments; DROP FUNCTION market.refuse_tcs();");
                return 0;
            });
        }

        (await SearchAsync("?q=NEWCO")).Items.ShouldBeEmpty("no part of the failed import is visible");
        (await SearchAsync("?q=RELIANCE-2026-10")).Items.Single().Retired.ShouldBeFalse();
        (await GetStatusAsync()).TotalActive.ShouldBe(60);
    }

    [Fact]
    public async Task A_sector_list_that_cannot_be_fetched_only_warns_and_the_sectors_stay()
    {
        await RefreshAsync();
        Factory.MasterData.SectorOverride = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var run = await RefreshAsync(force: true);

        run.Status.ShouldBe("succeeded");
        run.Warnings.ShouldContain(w => w.Contains("Sectors were not updated", StringComparison.Ordinal) && w.Contains("HTTP 403", StringComparison.Ordinal));
        (run.Added, run.Updated, run.Unchanged).ShouldBe((0, 0, 60));
        (await SearchAsync("?q=NSE:RELIANCE&type=share")).Items.Single().Sector.ShouldBe("Oil Gas & Consumable Fuels");
    }

    [Fact]
    public async Task A_first_import_without_a_sector_list_leaves_sectors_empty_and_warns()
    {
        Factory.MasterData.SectorOverride = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var run = await RefreshAsync();

        run.Status.ShouldBe("succeeded");
        run.Warnings.ShouldHaveSingleItem();
        (await FacetsAsync()).Sectors.ShouldHaveSingleItem().ShouldBe(new XDrishti.Application.Market.Search.FacetCount("Unclassified", 30));
    }

    [Fact]
    public async Task A_second_request_while_one_is_registered_gets_409_and_a_new_one_is_accepted_after_it_ended()
    {
        await StartRefreshAsync();

        using var second = await Client.PostAsync("/api/instruments/refresh", Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.refresh_running");
        Factory.RefreshJobs.PendingCount.ShouldBe(1);

        await Factory.RefreshJobs.RunPendingAsync(Factory.Services, Ct);

        await StartRefreshAsync();
    }

    [Fact]
    public async Task Simultaneous_requests_start_exactly_one_refresh()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Client.PostAsync("/api/instruments/refresh", Ct)));

        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
            Factory.RefreshJobs.PendingCount.ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_held_advisory_lock_makes_a_start_wait_its_turn_with_409()
    {
        // The lock a second instance would hold while it registers its own refresh.
        await using var other = new Npgsql.NpgsqlConnection(Factory.AdminConnectionString);
        await other.OpenAsync(Ct);
        await using (var hold = new Npgsql.NpgsqlCommand("SELECT pg_advisory_lock(8675179047521314164)", other))
        {
            await hold.ExecuteNonQueryAsync(Ct);
        }

        using var blocked = await Client.PostAsync("/api/instruments/refresh", Ct);

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ReadProblemAsync(Ct)).Code.ShouldBe("instruments.refresh_running");
        Factory.RefreshJobs.PendingCount.ShouldBe(0);

        await using (var release = new Npgsql.NpgsqlCommand("SELECT pg_advisory_unlock(8675179047521314164)", other))
        {
            await release.ExecuteNonQueryAsync(Ct);
        }

        await StartRefreshAsync();
    }

    [Fact]
    public async Task A_refresh_that_never_ended_stops_blocking_after_half_an_hour_and_is_marked_failed()
    {
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO market.master_refreshes (id, \"trigger\", \"force\", requested_by, started_at, status, phase, rows_read, added, updated, retired, revived, unchanged, skipped_by_scope, collisions, sector_matched, sector_unmatched, fno_underlyings, total_active, kept_by_type, warnings, rules_version, sectors_applied) "
                + "VALUES (gen_random_uuid(), 'manual', false, 'x', {0}, 'running', 'parsing', 10, 0,0,0,0,0,0,0,0,0,0,0, '{{}}'::jsonb, '[]'::jsonb, 1, false)",
                Now.AddMinutes(-45));
            return 0;
        });

        using var response = await Client.PostAsync("/api/instruments/refresh", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var history = await ListHistoryAsync();
        history.Count.ShouldBe(2);
        history.Single(h => h.Status == "failed").Error!.ShouldContain("did not finish");
    }

    [Fact]
    public async Task The_history_lists_the_runs_newest_first_and_honours_take()
    {
        await RefreshAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await RefreshAsync();
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await RefreshAsync(force: true);

        var history = await ListHistoryAsync();
        var onlyTwo = await ListHistoryAsync("?take=2");

        history.Select(h => (h.Trigger, h.Status)).ShouldBe([("forced", "succeeded"), ("manual", "unchanged"), ("manual", "succeeded")]);
        onlyTwo.Count.ShouldBe(2);
        (await ListHistoryAsync("?take=0")).Count.ShouldBe(1);
        (await ListHistoryAsync("?take=9999")).Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_downloads_are_plain_GETs_to_the_two_allow_listed_files_and_carry_no_credentials()
    {
        await RefreshAsync();

        var requests = Factory.MasterData.Requests.ToList();

        requests.Select(r => (r.Method, r.Uri.ToString())).ShouldBe(
        [
            (HttpMethod.Get, "https://images.dhan.co/api-data/api-scrip-master-detailed.csv"),
            (HttpMethod.Get, "https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv"),
        ]);
        foreach (var request in requests)
        {
            request.Header("User-Agent").ShouldBe("xDrishti/0.1 (personal use)");
            request.Headers.Keys.ShouldNotContain(k => k == "Authorization" || k == "Cookie" || k == "Proxy-Authorization" || k == "X-CSRF-Token");
            request.Uri.Query.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_logs_of_a_refresh_name_no_urls_and_carry_no_file_content()
    {
        await RefreshAsync(force: true);
        Factory.MasterData.MasterOverride = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await RefreshAsync(force: true);

        var logs = Factory.Logs.All;

        logs.ShouldNotContain(l => l.Contains("api-scrip-master-detailed.csv", StringComparison.Ordinal));
        logs.ShouldNotContain(l => l.Contains("ind_niftytotalmarket_list", StringComparison.Ordinal));
        logs.ShouldNotContain(l => l.Contains("RELIANCE INDUSTRIES", StringComparison.Ordinal));
        logs.ShouldContain(l => l.Contains("Instrument refresh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refresh_needs_a_session_but_no_step_up()
    {
        // The signed-in owner of the base class has not re-entered the password.
        using var response = await Client.PostAsync("/api/instruments/refresh", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task An_import_without_sectors_is_not_skipped_as_unchanged_next_time_so_the_sectors_get_filled_in()
    {
        Factory.MasterData.SectorOverride = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        (await RefreshAsync()).Warnings.ShouldNotBeEmpty();
        Factory.MasterData.SectorOverride = null;

        var second = await RefreshAsync();

        second.Status.ShouldBe("succeeded");
        (second.Added, second.Updated, second.Unchanged).ShouldBe((0, 13, 47)); // the 13 shares with a sector
        (await SearchAsync("?q=NSE:RELIANCE&type=share")).Items.Single().Sector.ShouldBe("Oil Gas & Consumable Fuels");
        (await RefreshAsync()).Status.ShouldBe("unchanged");
    }

    [Fact]
    public async Task A_run_made_under_older_rules_does_not_allow_the_unchanged_shortcut()
    {
        await RefreshAsync();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE market.master_refreshes SET rules_version = 0");
            return 0;
        });

        var second = await RefreshAsync();

        second.Status.ShouldBe("succeeded");
        (second.Added, second.Updated, second.Unchanged).ShouldBe((0, 0, 60));
        Factory.MasterData.MasterRequests.Last().Header("If-None-Match").ShouldBeEmpty();
        (await RefreshAsync()).Status.ShouldBe("unchanged");
    }

    [Fact]
    public async Task An_empty_list_is_never_skipped_as_unchanged()
    {
        await RefreshAsync();
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM market.instrument_broker_ids; DELETE FROM market.instruments");
            return 0;
        });

        var second = await RefreshAsync();

        second.Status.ShouldBe("succeeded");
        second.Added.ShouldBe(60);
        Factory.MasterData.MasterRequests.Last().Header("If-None-Match").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_that_cannot_be_read_is_skipped_with_a_warning_and_its_instrument_is_not_retired()
    {
        var padding = string.Join('\n', Enumerable.Range(0, 140).Select(i => ShareRow((800000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "PAD" + i, "PAD " + i + " LTD", "INE8" + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + "01")));
        Factory.MasterData.MasterCsv += "\n" + padding;
        var first = await RefreshAsync();
        first.Added.ShouldBe(200);
        Factory.MasterData.MasterCsv = WithLine(Factory.MasterData.MasterCsv, "TATA CONSULTANCY SERV LT", line => line.Replace(",ES,EQ,1.0,", ",ES,EQ,zero,", StringComparison.Ordinal));
        Factory.MasterData.MasterETag = "\"dhan-v2\"";

        var second = await RefreshAsync();

        second.Status.ShouldBe("succeeded");
        second.Warnings.ShouldContain(w => w.StartsWith("Skipped Dhan id 11536:", StringComparison.Ordinal) && w.Contains("lot size", StringComparison.Ordinal));
        (second.Retired, second.Added, second.Updated, second.Unchanged).ShouldBe((0, 0, 0, 199));
        (await SearchAsync("?q=NSE:TCS&type=share")).Items.Single().Retired.ShouldBeFalse();
        (await GetStatusAsync()).TotalActive.ShouldBe(200);
    }

    [Fact]
    public async Task A_file_that_would_retire_most_of_the_list_is_refused_unless_forced()
    {
        await RefreshAsync();
        var header = Factory.MasterData.MasterCsv.Split('\n')[0];
        var few = string.Join('\n', Factory.MasterData.MasterCsv.Split('\n').Where(l => l.Contains(",EQUITY,", StringComparison.Ordinal) && l.Contains(",ES,EQ,", StringComparison.Ordinal)).Take(10));
        Factory.MasterData.MasterCsv = header + "\n" + few + "\n";
        Factory.MasterData.MasterETag = "\"dhan-v2\"";

        var refused = await RefreshAsync();

        refused.Status.ShouldBe("failed");
        refused.Error.ShouldBe("The new list has only 10 instruments while 60 are active; it was not applied. If the file is right, run a forced refresh.");
        (await GetStatusAsync()).TotalActive.ShouldBe(60);

        var forced = await RefreshAsync(force: true);

        forced.Status.ShouldBe("succeeded");
        (forced.Retired, forced.TotalActive).ShouldBe((50, 10));
    }

    [Fact]
    public async Task A_tolerated_extra_column_shows_as_a_warning_and_the_import_goes_on()
    {
        Factory.MasterData.MasterCsv = Factory.MasterData.MasterCsv.Replace("SM_FREEZE_QTY,", "SM_FREEZE_QTY,NEW_FLAG,", StringComparison.Ordinal);
        // Rows would need the extra field; give them one.
        Factory.MasterData.MasterCsv = string.Join('\n', Factory.MasterData.MasterCsv.Split('\n').Select((l, i) => i == 0 || l.Length == 0 ? l : l.TrimEnd(',') + ",x,"));

        var run = await RefreshAsync();

        run.Status.ShouldBe("succeeded");
        run.Added.ShouldBe(60);
        run.Warnings.ShouldHaveSingleItem().ShouldContain("NEW_FLAG");
    }

    private const string FakeOwnerEmail = ApiFactory.OwnerEmail;

    private static string ShareRow(string id, string ticker, string name, string isin) =>
        $"NSE,E,{id},{isin},EQUITY,,{ticker},{name},{name},ES,EQ,1.0,,,,5.0000,NA,N,N,N,NA,A,0,0,0,0,0,0,3.3,100.0,50.0,1000,";

    private async Task<List<RefreshRunView>> ListHistoryAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/instruments/refresh/history" + query, Ct);
        return [.. await response.ReadAsync<List<RefreshRunView>>(Ct)];
    }

    private async Task<DateTimeOffset> LastSeenAsync() =>
        await Factory.WithDbAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync(Ct);
            command.CommandText = "SELECT max(last_seen_at) FROM market.instruments";
            return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(Ct))!);
        });
}
