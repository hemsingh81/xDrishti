using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The Dhan phase on top of a fake source (the real client is behind the same port): what is asked for, how the bars are checked, compared with the stored ones and
/// merged, and how failures end the phase or only one instrument.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataDhanPhaseTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Brokers.Reset();
    }

    private async Task<Guid> TrackedWithBarsAsync(string ticker, List<Bar> bars)
    {
        var id = await SeedAsync(ticker);
        WriteCsv(ticker, bars);
        (await RunAsync(["csv"])).Csv.Status.ShouldBe("succeeded");
        return id;
    }

    private static IntradayResult Bars(IntradayTarget target, IEnumerable<Bar> bars, int requests = 1) => new(target.InstrumentId, [.. bars], requests);

    [Fact]
    public async Task Without_a_data_account_the_real_source_is_not_asked_the_phase_is_skipped_and_the_job_succeeds()
    {
        await SeedAsync("ABB");

        var job = await RunAsync(["dhan"]);

        job.Status.ShouldBe("succeeded");
        (job.Dhan.Status, job.Dhan.Code, job.Dhan.Message).ShouldBe(("skipped", "data.dhan_not_connected", "No account has the primary Data role. Add or choose one under Accounts."));
        Factory.Brokers.Requests.ShouldBeEmpty();
        (job.Dhan.Done, job.Dhan.Total, job.Dhan.Bars).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Without_a_connected_data_account_the_phase_is_skipped_and_asks_the_owner_to_reconnect()
    {
        var source = Factory.Dhan.UseSource(t => Bars(t, []));
        Factory.Dhan.UseAccount(new DataAccountState(Guid.NewGuid(), "Primary", DataAccountStatuses.Expired, "The Dhan sign-in of the Data account 'Primary' has expired. Reconnect it under Accounts."));
        await SeedAsync("ABB");

        var job = await RunAsync(["dhan"]);

        (job.Status, job.Dhan.Status, job.Dhan.Code).ShouldBe(("succeeded", "skipped", "data.dhan_not_connected"));
        job.Dhan.Message!.ShouldContain("Reconnect it under Accounts");
        job.Dhan.AccountName.ShouldBe("Primary");
        source.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_nothing_tracked_that_has_a_dhan_id_there_is_nothing_to_fetch()
    {
        var source = Factory.Dhan.UseSource(t => Bars(t, []));
        Factory.Dhan.UseConnectedAccount();
        await SeedAsync("ABB");

        var job = await RunAsync(["dhan"]);

        (job.Dhan.Status, job.Dhan.Code).ShouldBe(("skipped", null));
        job.Dhan.Message.ShouldBe("No tracked share, ETF or index has a Dhan id: nothing to fetch.");
        source.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_plan_starts_two_days_before_the_last_bar_or_the_initial_days_back_in_windows_of_at_most_90_days()
    {
        var abb = await TrackedWithBarsAsync("ABB", BarFixtures.Session(LastSession, 1, count: 100));
        var tcs = await SeedAsync("TCS");
        await TrackAsync(tcs);
        await SeedAsync("INDEXLESS", dhanId: false);
        var source = Factory.Dhan.UseSource(t => Bars(t, []));
        Factory.Dhan.UseConnectedAccount("Primary");

        var job = await RunAsync(["dhan"]);

        job.Dhan.Status.ShouldBe("succeeded", job.Dhan.Message);
        (job.Dhan.AccountName, job.Dhan.Total, job.Dhan.Done, job.Dhan.InstrumentsFetched, job.Dhan.Requests).ShouldBe(("Primary", 2, 2, 2, 2));
        var plan = source.Plans.ShouldHaveSingleItem();
        plan.Targets.Select(t => t.Symbol).ShouldBe(["NSE:ABB", "NSE:TCS"]);
        var withBars = plan.Targets[0];
        var lastBar = BarFixtures.Session(LastSession, 1, count: 100)[^1].EpochSeconds;
        withBars.Windows.ShouldHaveSingleItem().From.ToUnixTimeSeconds().ShouldBe(lastBar - 2 * 86400);
        withBars.Windows[0].To.ShouldBe(Now, TimeSpan.FromSeconds(5));
        withBars.InstrumentId.ShouldBe(abb);
        var withoutBars = plan.Targets[1];
        withoutBars.Windows.Count.ShouldBe(5); // 365 days in windows of 90
        withoutBars.Windows[0].From.ShouldBe(Now.AddDays(-365), TimeSpan.FromSeconds(5));
        withoutBars.Windows.ShouldAllBe(w => w.To - w.From <= TimeSpan.FromDays(90));
        withoutBars.Windows.Zip(withoutBars.Windows.Skip(1)).ShouldAllBe(p => p.First.To == p.Second.From);
        (withoutBars.SecurityId, withoutBars.Segment, withoutBars.Type).ShouldBe((withoutBars.SecurityId, "NSE_EQ", "share"));
    }

    [Fact]
    public async Task Fetched_bars_are_stored_replace_csv_bars_and_the_overlap_is_compared_with_a_tolerance()
    {
        var csvBars = BarFixtures.Session(LastSession, 1, count: 60);
        var abb = await TrackedWithBarsAsync("ABB", csvBars);
        var nextDay = new DateOnly(2026, 10, 12);
        var newBars = BarFixtures.Session(nextDay, 7, count: 20);
        var overlap = csvBars.Skip(40).ToList(); // the last 20 minutes of the CSV, as Dhan reports them
        overlap[0] = overlap[0] with { Close = overlap[0].Close * 1.01, High = overlap[0].High * 1.02 };       // 1 % off: a mismatch
        overlap[1] = overlap[1] with { Close = overlap[1].Close * 1.0002, Volume = overlap[1].Volume + 500 };  // within 0.05 %, volume ignored
        overlap[2] = overlap[2] with { Open = overlap[2].Open - 1.0 };                                          // about 1 % off: a mismatch
        overlap[3] = overlap[3] with { Open = overlap[3].Open - 0.01 };                                         // 0.01 % off: tolerated
        Factory.Dhan.UseSource(t => Bars(t, [.. overlap, .. newBars], requests: 2));
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan", "aggregate"]);

        job.Dhan.Status.ShouldBe("succeeded", job.Dhan.Message);
        (job.Dhan.Bars, job.Dhan.Inserted, job.Dhan.Updated, job.Dhan.Unchanged).ShouldBe((40, 20, 4, 16));
        (job.Dhan.OverlapCompared, job.Dhan.OverlapMismatched, job.Dhan.Requests).ShouldBe((20, 2, 2));
        job.Warnings.ShouldHaveSingleItem().ShouldBe("NSE:ABB: 2 of 20 overlapping bars differ from the stored ones by more than 0.05 %.");
        var stored = await StoredBarsAsync(abb);
        stored.Count.ShouldBe(80);
        stored[41].Close.ShouldBe(overlap[1].Close);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m WHERE source = 'dhan'")).ShouldBe(40);
        job.Aggregate.Done.ShouldBe(1);
        var overview = await OverviewAsync();
        (overview.Dhan.LastFetchBars, overview.Dhan.LastFetchAt).ShouldBe((20, job.Dhan.FinishedAt));
        (await InstrumentsAsync()).Items.Single().Source.ShouldBe("mixed");
    }

    [Fact]
    public async Task Bars_outside_a_session_or_breaking_the_row_rules_are_not_stored_and_a_repeated_minute_is_kept_once()
    {
        var abb = await SeedAsync("ABB");
        await TrackAsync(abb);
        var good = BarFixtures.Session(LastSession, 1, count: 5);
        var outside = new Bar(BarFixtures.Epoch(LastSession, 3 * 60), 100, 101, 99, 100, 5);            // 03:00 India
        var shifted = new Bar(good[0].EpochSeconds + 5 * 3600 + 1800 + 60 * 400, 100, 101, 99, 100, 5);  // ~ 5.5 h later: after the close
        var broken = new Bar(good[0].EpochSeconds + 10 * 60, -1, 101, 99, 100, 5);
        var repaired = new Bar(good[0].EpochSeconds + 11 * 60, 105, 101, 99, 100, 5);
        var evening = new Bar(BarFixtures.Epoch(LastSession, 18 * 60 + 15), 100, 101, 99, 100, 5);        // a special evening session is fine
        Factory.Dhan.UseSource(t => Bars(t, [good[0], good[0], .. good, outside, shifted, broken, repaired, evening]));
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan"]);

        (job.Dhan.Bars, job.Dhan.Inserted, job.Dhan.Repaired, job.Dhan.Rejected).ShouldBe((12, 7, 1, 3));
        job.Warnings.ShouldHaveSingleItem().ShouldStartWith("NSE:ABB: 3 bars from Dhan were not stored (for example ");
        (await StoredBarsAsync(abb)).Select(b => b.EpochSeconds).ShouldBe([.. good.Select(b => b.EpochSeconds), repaired.EpochSeconds, evening.EpochSeconds]);
        (await StoredBarsAsync(abb)).Single(b => b.EpochSeconds == repaired.EpochSeconds).High.ShouldBe(105);
    }

    [Fact]
    public async Task One_instrument_failing_is_recorded_and_the_others_still_load()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        await TrackAsync(abb, tcs);
        var good = BarFixtures.Session(LastSession, 1, count: 5);
        Factory.Dhan.UseSource(t => t.Symbol == "NSE:ABB"
            ? new IntradayResult(t.InstrumentId, [], 3, new IntradayFailure(IntradayFailureKind.Failed, "Dhan answered 500 for the window 2026-07-01"))
            : Bars(t, good));
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan"]);

        job.Status.ShouldBe("succeeded");
        (job.Dhan.Status, job.Dhan.InstrumentsFetched, job.Dhan.InstrumentsFailed, job.Dhan.Done, job.Dhan.Total).ShouldBe(("succeeded", 1, 1, 2, 2));
        job.Dhan.Errors.ShouldBe(["NSE:ABB: Dhan answered 500 for the window 2026-07-01"]);
        job.Dhan.Message.ShouldBe("1 of 2 instruments could not be fetched: NSE:ABB: Dhan answered 500 for the window 2026-07-01");
        (await StoredBarsAsync(tcs)).ShouldBe(good);
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
    }

    [Fact]
    public async Task When_every_instrument_fails_the_phase_and_the_job_fail_but_the_aggregates_still_run()
    {
        var abb = await SeedAsync("ABB");
        await TrackAsync(abb);
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "timeout")));
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan", "aggregate"]);

        (job.Status, job.Dhan.Status).ShouldBe(("failed", "failed"));
        job.Error.ShouldBe("Dhan returned errors for all 1 instruments: NSE:ABB: timeout");
        job.Aggregate.Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task A_refused_token_ends_the_phase_with_data_dhan_not_connected_and_the_rest_is_not_fetched()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        await TrackAsync(abb, tcs);
        var asked = new List<string>();
        Factory.Dhan.UseSource(t =>
        {
            asked.Add(t.Symbol);
            return new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Unauthorized, "token refused"));
        });
        Factory.Dhan.UseConnectedAccount();

        var job = await RunAsync(["dhan", "aggregate"]);

        (job.Status, job.Dhan.Status, job.Dhan.Code).ShouldBe(("failed", "failed", "data.dhan_not_connected"));
        job.Dhan.Message.ShouldBe("Dhan refused the access token of the Data account. Reconnect it under Accounts, then run again.");
        (job.Dhan.Done, job.Dhan.InstrumentsFailed).ShouldBe((0, 0));
        asked.ShouldBe(["NSE:ABB"]);
        job.Aggregate.Status.ShouldBe("succeeded");
    }

    [Fact]
    public async Task Fetching_again_stores_nothing_new_and_a_csv_load_afterwards_does_not_take_the_bars_back()
    {
        var csvBars = BarFixtures.Session(LastSession, 1, count: 30);
        var abb = await TrackedWithBarsAsync("ABB", csvBars);
        var dhanBars = csvBars.Select(b => b with { Close = b.High, Volume = b.Volume + 1 }).ToList();
        Factory.Dhan.UseSource(t => Bars(t, dhanBars));
        Factory.Dhan.UseConnectedAccount();
        var first = await RunAsync(["dhan"]);

        var second = await RunAsync(["dhan"]);
        var reloaded = await RunAsync(["csv"], forceCsv: true);

        (first.Dhan.Inserted, first.Dhan.Updated).ShouldBe((0, 30));
        (second.Dhan.Inserted, second.Dhan.Updated, second.Dhan.Unchanged).ShouldBe((0, 0, 30));
        (reloaded.Csv.KeptDhan, reloaded.Csv.Inserted, reloaded.Csv.Updated).ShouldBe((30, 0, 0));
        (await StoredBarsAsync(abb)).ShouldBe(dhanBars);
    }

    private async Task TrackAsync(params Guid[] ids)
    {
        using var response = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct);
        response.EnsureSuccessStatusCode();
    }
}
