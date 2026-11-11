using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Tests.Market.Baskets.Systems;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Tests.Market.Baskets.Lists;

/// <summary>The ban file and the lot-size file through the same fetch handler as the index lists (BK-05).</summary>
public sealed class ListFileFetchTests
{
    private readonly ListWorld _w = new();

    private static readonly DateTimeOffset StartOf27Oct = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();

    private static string Problem(FetchOutcomeView outcome) => $"{outcome.Outcome}:{outcome.Error}";

    // ---- the gates ----

    [Fact]
    public async Task With_the_lists_setting_off_neither_nse_file_is_requested_and_nothing_is_recorded()
    {
        _w.ListSettings.FetchEnabled = false;
        _w.BanSource();
        _w.LotSource();
        _w.Serve(ListWorld.Ban("SAIL"));

        var run = await _w.RunAsync();

        run.Value.Outcomes.ShouldBeEmpty();
        (_w.Fetcher.Calls, _w.Store.Recorded.Count, _w.Applier.Applied.Count).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task With_both_settings_off_the_run_is_refused_before_anything_is_asked()
    {
        _w.ListSettings.FetchEnabled = false;
        _w.Settings.Enabled = false;
        _w.BanSource();

        var run = await _w.RunAsync();

        run.Error.Code.ShouldBe(BasketErrors.FetchDisabledCode);
        (_w.Fetcher.Calls, _w.Store.ListCalls).ShouldBe((0, 0));
    }

    [Fact]
    public async Task A_manual_fetch_of_a_list_file_is_also_refused_while_its_setting_is_off()
    {
        _w.ListSettings.FetchEnabled = false;
        _w.BanSource();
        _w.Serve(ListWorld.Ban("SAIL"));

        var run = await _w.RunAsync(ListWorld.BanId);

        run.Value.Outcomes.ShouldBeEmpty();
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_lists_setting_alone_fetches_the_list_files_without_the_index_setting()
    {
        _w.Settings.Enabled = false;
        _w.Share("SAIL");
        _w.BanSource();
        _w.Serve(ListWorld.Ban("SAIL"));

        var outcome = await _w.RunOneAsync();

        outcome.Outcome.ShouldBe(FetchOutcomes.Applied);
        _w.Fetcher.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_list_read_from_the_master_is_never_fetched()
    {
        _w.FlagSource(ListBaskets.FnoStocks);

        var run = await _w.RunAsync();

        run.Value.Outcomes.ShouldBeEmpty();
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_ban_file_is_asked_at_19_00_and_08_30_not_between_and_the_lot_file_at_08_50()
    {
        _w.Share("SAIL");
        _w.BanSource(checkedAt: _w.Clock.GetUtcNow().AddMinutes(-4)); // asked at 19:01
        _w.LotSource(checkedAt: _w.Clock.GetUtcNow().AddMinutes(-4));
        _w.Serve(ListWorld.Ban("SAIL"));

        (await _w.RunAsync()).Value.Outcomes.ShouldBeEmpty();

        _w.Clock.Advance(TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(-5))); // 08:00 next day: 08:30 not reached
        (await _w.RunAsync()).Value.Outcomes.ShouldBeEmpty();

        _w.Clock.Advance(TimeSpan.FromMinutes(35)); // 08:35: the ban retry is due, the lot file (08:50) is not
        var morning = await _w.RunAsync();
        morning.Value.Outcomes.Select(o => o.BasketId).ShouldBe([ListWorld.BanId]);

        _w.Clock.Advance(TimeSpan.FromMinutes(20)); // 08:55: the lot file is due
        _w.Serve(ListWorld.Lot(("SAIL", "SAIL", "4700")));
        (await _w.RunAsync()).Value.Outcomes.Select(o => o.BasketId).ShouldContain(ListWorld.LotsId);
    }

    // ---- the ban list ----

    [Fact]
    public async Task Three_banned_symbols_are_matched_to_shares_and_dated_by_the_trade_date_in_the_file()
    {
        var ids = new[] { _w.Share("AMBUJACEM"), _w.Share("KAYNES"), _w.Share("SAIL") };
        var yesterdays = _w.Share("OLDBAN");
        _w.Member(ListWorld.BanId, yesterdays, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)));
        _w.BanSource();
        _w.Serve(ListWorld.Ban("AMBUJACEM", "KAYNES", "SAIL"));

        var outcome = await _w.RunOneAsync();

        outcome.Outcome.ShouldBe(FetchOutcomes.Applied);
        var application = _w.Applier.Applied.Single();
        application.Change.Added.Order().ShouldBe(ids.Order());
        application.Change.Removed.ShouldBe([yesterdays]); // the previous day's list closes on the same date
        application.EffectiveFrom.ShouldBe(StartOf27Oct); // the NSE-stated trade date, not the fetch day
        (application.Origin!.Provenance, application.Origin.SourceKind).ShouldBe((BasketProvenance.Fetched, "fetch"));
        application.Fetch.Rows.ShouldBe(3);
    }

    [Fact]
    public async Task A_file_with_the_first_line_only_is_an_empty_valid_list_that_closes_the_members()
    {
        var old = _w.Share("OLDBAN");
        _w.Member(ListWorld.BanId, old, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)));
        _w.BanSource();
        _w.Serve(ListWorld.BanFirstLine);

        var outcome = await _w.RunOneAsync();

        outcome.Outcome.ShouldBe(FetchOutcomes.Applied);
        _w.Applier.Applied.Single().Change.Removed.ShouldBe([old]);
    }

    [Fact]
    public async Task An_empty_file_on_an_empty_list_is_unchanged_not_a_rejection()
    {
        _w.BanSource();
        _w.Serve(ListWorld.BanFirstLine);

        (await _w.RunOneAsync()).Outcome.ShouldBe(FetchOutcomes.Unchanged);
    }

    [Fact]
    public async Task A_first_line_without_a_parseable_date_is_rejected_and_nothing_is_applied()
    {
        _w.Share("SAIL");
        _w.BanSource();
        _w.Serve("Securities in Ban For Trade Date soon:\r\n1,SAIL\r\n");

        var outcome = await _w.RunOneAsync();

        Problem(outcome).ShouldBe("rejected:bad_date");
        _w.Applier.Applied.ShouldBeEmpty();
        _w.Store.Recorded.Single().Source.State.ShouldBe(BasketSourceStates.Rejected);
    }

    [Fact]
    public async Task A_trade_date_far_from_today_is_rejected()
    {
        _w.BanSource();
        _w.Serve(ListWorld.BanFirstLine.Replace("27-OCT-2026", "27-OCT-2025", StringComparison.Ordinal));

        Problem(await _w.RunOneAsync()).ShouldBe("rejected:bad_date");
    }

    [Fact]
    public async Task A_file_older_than_the_last_applied_trade_date_is_rejected_as_stale_even_after_an_empty_list_day()
    {
        _w.Share("SAIL");
        _w.BanSource(knownThrough: new DateOnly(2026, 10, 27)); // the last applied file was the empty list of 27 Oct: no member holds the date
        _w.Serve(ListWorld.Ban("SAIL").Replace("27-OCT-2026", "25-OCT-2026", StringComparison.Ordinal));

        Problem(await _w.RunOneAsync()).ShouldBe("rejected:stale_trade_date");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_applied_or_confirmed_file_moves_the_known_through_date_to_its_trade_date_and_a_rejected_one_does_not()
    {
        _w.Share("SAIL");
        _w.BanSource();
        _w.Serve(ListWorld.Ban("SAIL"));

        await _w.RunOneAsync();
        _w.Applier.Applied.Single().Source.KnownThrough.ShouldBe(new DateOnly(2026, 10, 27));

        _w.Serve("Securities in Ban For Trade Date soon:\r\n");
        await _w.RunOneAsync();
        _w.Store.Recorded.Last().Source.KnownThrough.ShouldBeNull(); // null leaves the stored date as it is
    }

    [Fact]
    public async Task The_same_bytes_a_second_time_are_unchanged_and_a_symbol_not_in_the_master_is_unmatched_and_listed()
    {
        _w.Share("SAIL");
        var csv = ListWorld.Ban("SAIL", "GHOSTCO");
        _w.BanSource();
        _w.Serve(csv);

        var first = await _w.RunOneAsync();
        var application = _w.Applier.Applied.Single();

        first.Unmatched.ShouldBe(1);
        application.Fetch.WarningsJson.ShouldContain("unmatched:GHOSTCO");

        // the 08:30 fetch of the same bytes
        _w.BanSource(checkedAt: _w.Clock.GetUtcNow(), sha: FetchWorld.Sha(csv));
        _w.Clock.Advance(TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(30)));
        var second = await _w.RunOneAsync();

        second.Outcome.ShouldBe(FetchOutcomes.Unchanged);
        _w.Applier.Applied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_404_page_is_rejected_whether_it_arrives_as_404_or_as_html_with_200()
    {
        _w.BanSource();
        _w.Serve("<html>Not Found</html>", status: 404, contentType: "text/html");
        Problem(await _w.RunOneAsync()).ShouldBe("rejected:http_404");

        _w.Serve("<html>Not Found</html>", contentType: "text/html; charset=utf-8");
        Problem(await _w.RunOneAsync()).ShouldBe("rejected:not_csv");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_ban_rows_than_the_bounds_of_the_source_are_rejected()
    {
        _w.Share("A");
        _w.Share("B");
        _w.BanSource(max: 1);
        _w.Serve(ListWorld.Ban("A", "B"));

        Problem(await _w.RunOneAsync()).ShouldBe("rejected:row_count");
    }

    [Fact]
    public async Task A_ban_list_may_replace_every_member_in_one_day_without_the_index_tripwire()
    {
        var olds = Enumerable.Range(0, 6).Select(i => _w.Share($"OLD{i}")).ToList();
        foreach (var id in olds)
        {
            _w.Member(ListWorld.BanId, id, new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)));
        }

        _w.Share("NEWBAN");
        _w.BanSource();
        _w.Serve(ListWorld.Ban("NEWBAN"));

        (await _w.RunOneAsync()).Outcome.ShouldBe(FetchOutcomes.Applied);
    }

    [Fact]
    public async Task A_banned_name_in_an_open_plan_changes_nothing_here_because_the_basket_is_only_an_input()
    {
        _w.Share("SAIL");
        _w.BanSource();
        _w.Serve(ListWorld.Ban("SAIL"));

        await _w.RunOneAsync();

        _w.Applier.Applied.ShouldHaveSingleItem().BasketId.ShouldBe(ListWorld.BanId); // the only write is the ban list's own membership
    }

    // ---- the lot sizes ----

    [Fact]
    public async Task The_lot_file_is_recorded_as_a_summary_with_index_rows_skipped_and_the_share_lots_handed_to_the_lot_transaction()
    {
        _w.Share("RELIANCE", fno: true, lot: 250);
        _w.Share("SAIL", fno: true, lot: 4700);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("NIFTY BANK", "BANKNIFTY", "30"), ("RELIANCE INDUSTRIES", "RELIANCE", "250"), ("SAIL", "SAIL", "4700")));

        var outcome = await _w.RunOneAsync();

        outcome.Outcome.ShouldBe(FetchOutcomes.Applied); // two lot rows written (the fake answers the count it was given)
        outcome.Rows.ShouldBe(2);
        var fetch = _w.LotApplier.Applied.Single().Fetch(2);
        fetch.WarningsJson.ShouldContain("lots_summary:shares=2,nearest=OCT-26,mismatches=0,withoutLot=0,indexUnmatched=1,lotsWritten=2");
        fetch.WarningsJson.ShouldContain("index_rows_skipped:1");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lot_that_differs_from_the_master_is_a_warning_naming_both_values_and_the_master_is_not_touched()
    {
        _w.Share("RELIANCE", fno: true, lot: 250);
        _w.Share("SAIL", fno: true, lot: 4700);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("RELIANCE INDUSTRIES", "RELIANCE", "275"), ("SAIL", "SAIL", "4700")));

        await _w.RunOneAsync();

        var warnings = _w.Store.Recorded.Single().Fetch.WarningsJson;
        warnings.ShouldContain("lot_size_mismatch:RELIANCE:master=250:nse=275");
        warnings.ShouldNotContain("lot_size_mismatch:SAIL");
        warnings.ShouldContain("mismatches=1");
        _w.Master.Shares.Single(s => s.Symbol == "RELIANCE").MasterLot.ShouldBe(250); // the master is primary and has no write path here
    }

    [Fact]
    public async Task Equal_lots_raise_no_warning()
    {
        _w.Share("RELIANCE", fno: true, lot: 250);
        _w.Share("SAIL", fno: true, lot: 4700);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("RELIANCE INDUSTRIES", "RELIANCE", "250"), ("SAIL", "SAIL", "4700")));

        await _w.RunOneAsync();

        _w.Store.Recorded.Single().Fetch.WarningsJson.ShouldNotContain("lot_size_mismatch");
    }

    [Fact]
    public async Task The_universe_difference_is_a_warning_and_the_membership_is_not_corrected()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        _w.Share("C", fno: true, lot: 10); // the master has three, the NSE file two
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("A", "A", "10"), ("B", "B", "10")));

        await _w.RunOneAsync();

        _w.Store.Recorded.Single().Fetch.WarningsJson.ShouldContain("fo_universe_diff:onlyMaster=1,onlyNse=0");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<html>404</html>", "bad_header")]
    [InlineData("UNDERLYING,SYMBOL,OCT-26\r\nA,A,many\r\n", "bad_lot")]
    [InlineData("", "empty")]
    public async Task A_wrong_page_a_bad_lot_or_an_empty_file_is_rejected_and_nothing_is_stored(string body, string reason)
    {
        _w.Share("A", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(body.Length == 0 ? "\r\n" : body);

        var outcome = await _w.RunOneAsync();

        Problem(outcome).ShouldBe($"rejected:{reason}");
        _w.Store.Recorded.Single().Fetch.Outcome.ShouldBe(FetchOutcomes.Rejected);
    }

    [Theory]
    [InlineData(1)] // below the bound of 2
    [InlineData(11)] // above the bound of 10
    public async Task A_share_underlying_count_outside_the_bounds_of_the_source_is_rejected(int shares)
    {
        var rows = new List<(string, string, string)>();
        for (var i = 0; i < shares; i++)
        {
            _w.Share($"S{i:00}", fno: true, lot: 10);
            rows.Add(($"S{i:00}", $"S{i:00}", "10"));
        }

        _w.LotSource();
        _w.Serve(ListWorld.Lot([.. rows]));

        Problem(await _w.RunOneAsync()).ShouldBe("rejected:row_count");
    }

    [Fact]
    public async Task A_failed_request_keeps_everything_and_retries_after_the_backoff()
    {
        _w.LotSource();
        _w.Fetcher.Response = SourceResponse.Unreachable("timeout");

        var outcome = await _w.RunOneAsync();

        Problem(outcome).ShouldBe("failed:timeout");
        _w.Store.Recorded.Single().Source.NextAttemptAt.ShouldBe(_w.Clock.GetUtcNow().AddMinutes(5));
    }

    // ---- BK-05b: the NSE lots into options.lot_sizes (the transaction itself is covered by the database tests) ----

    [Fact]
    public async Task Each_share_row_matched_in_the_master_becomes_one_lot_for_its_instrument_with_the_file_url_and_the_fetch_day()
    {
        var reliance = _w.Share("RELIANCE", fno: true, lot: 250);
        var sail = _w.Share("SAIL", fno: true, lot: 4700);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("RELIANCE INDUSTRIES", "RELIANCE", "275"), ("SAIL", "SAIL", "4700"), ("UNKNOWN CO", "NOTINMASTER", "10")));

        await _w.RunOneAsync();

        var application = _w.LotApplier.Applied.Single();
        application.Lots.ShouldBe([new NseLot(reliance, 275), new NseLot(sail, 4700)], ignoreOrder: true); // an unmatched share has no row
        application.Today.ShouldBe(new DateOnly(2026, 10, 26));
        application.SourceUrl.ShouldBe("https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv");
        application.Fetch(2).WarningsJson.ShouldContain("lot_size_mismatch:RELIANCE:master=250:nse=275"); // the mismatch warning stays; the master is only read
    }

    [Fact]
    public async Task The_provenance_url_is_the_address_that_answered_not_the_catalogue_one()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("A", "A", "10"), ("B", "B", "10")));
        _w.Fetcher.Response = _w.Fetcher.Response with { Url = "https://alt.example/fo_mktlots.csv" };

        await _w.RunOneAsync();

        _w.LotApplier.Applied.Single().SourceUrl.ShouldBe("https://alt.example/fo_mktlots.csv");
    }

    [Fact]
    public async Task An_index_row_is_stored_only_when_its_canonical_symbol_names_one_index_of_the_master_otherwise_it_is_counted_unmatched()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        var nifty = Guid.CreateVersion7();
        _w.Master.Indices["NSE:NIFTY"] = nifty;
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("NIFTY 50", "NIFTY", "65"), ("NIFTY BANK", "BANKNIFTY", "30"), ("NIFTY NEXT 50", "NIFTYNXT50", "25"), ("A", "A", "10"), ("B", "B", "10")));

        await _w.RunOneAsync();

        var application = _w.LotApplier.Applied.Single();
        application.Lots.ShouldContain(new NseLot(nifty, 65));
        application.Lots.Count.ShouldBe(3); // A, B and NIFTY: BANKNIFTY is not in the master, NIFTYNXT50 has no mapping
        application.Fetch(3).WarningsJson.ShouldContain("indexUnmatched=2");
    }

    [Fact]
    public async Task When_the_transaction_writes_no_row_the_lots_are_unchanged_and_the_source_is_ok()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("A", "A", "10"), ("B", "B", "10")));
        _w.LotApplier.Written = 0;

        var outcome = await _w.RunOneAsync();

        outcome.Outcome.ShouldBe(FetchOutcomes.Unchanged);
        _w.LotApplier.Applied.Single().Fetch(0).WarningsJson.ShouldContain("lotsWritten=0");
    }

    [Fact]
    public async Task A_failed_lot_transaction_is_a_failed_fetch_and_the_source_shows_failed()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("A", "A", "10"), ("B", "B", "10")));
        _w.LotApplier.Fails = true;

        var outcome = await _w.RunOneAsync();

        Problem(outcome).ShouldBe("failed:lot_write_failed");
        var recorded = _w.Store.Recorded.Single();
        recorded.Source.State.ShouldBe(BasketSourceStates.Failed);
        recorded.Source.Sha256.ShouldBeNull(); // the old validators stay, the same file is looked at again
    }

    [Theory]
    [InlineData("<html>404</html>")]
    [InlineData("UNDERLYING,SYMBOL,OCT-26\r\nA,A,many\r\n")]
    public async Task A_rejected_lot_file_hands_nothing_to_the_lot_transaction(string body)
    {
        _w.Share("A", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(body);

        await _w.RunOneAsync();

        _w.LotApplier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_share_count_outside_the_bounds_hands_nothing_to_the_lot_transaction()
    {
        _w.Share("S", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("S", "S", "10")));

        Problem(await _w.RunOneAsync()).ShouldBe("rejected:row_count");

        _w.LotApplier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_bytes_twice_are_unchanged_and_hand_nothing_to_the_lot_transaction()
    {
        _w.Share("A", fno: true, lot: 10);
        _w.Share("B", fno: true, lot: 10);
        var body = ListWorld.Lot(("A", "A", "10"), ("B", "B", "10"));
        _w.LotSource();
        _w.Store.Sources[0] = _w.Store.Sources[0] with { Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))) };
        _w.Serve(body);

        (await _w.RunOneAsync()).Outcome.ShouldBe(FetchOutcomes.Unchanged);

        _w.LotApplier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_lists_setting_off_the_lot_transaction_is_never_reached()
    {
        _w.ListSettings.FetchEnabled = false;
        _w.Share("A", fno: true, lot: 10);
        _w.LotSource();
        _w.Serve(ListWorld.Lot(("A", "A", "10")));

        (await _w.RunAsync()).Value.Outcomes.ShouldBeEmpty();

        _w.LotApplier.Applied.ShouldBeEmpty();
        _w.Fetcher.Calls.ShouldBe(0);
    }
}
