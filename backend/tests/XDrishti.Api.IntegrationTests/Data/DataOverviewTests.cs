using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>What the Data &amp; import screen reads above its table: the three phases, the stored bars, how far behind they are and what the Dhan phase can do.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataOverviewTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task With_nothing_loaded_the_overview_is_empty_and_says_why_the_dhan_fetch_cannot_run()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        WriteCsv("TCS", BarFixtures.Session(LastSession, 1, count: 5), gzip: false);
        File.WriteAllText(CsvPath("readme.txt"), "ignored");

        var overview = await OverviewAsync();

        overview.Csv.Folder.ShouldBe(Factory.CsvFolder);
        (overview.Csv.FolderExists, overview.Csv.FilesTotal, overview.Csv.FilesLoaded, overview.Csv.FilesPending, overview.Csv.Rows, overview.Csv.LastLoadAt)
            .ShouldBe((true, 2, 0, 2, 0, null));
        overview.Dhan.Account.ShouldBeNull();
        (overview.Dhan.CanFetch, overview.Dhan.Reason, overview.Dhan.ReasonCode)
            .ShouldBe((false, "No account has the primary Data role. Add or choose one under Accounts.", "data.dhan_not_connected"));
        (overview.Dhan.InitialDays, overview.Dhan.RequestsPerSecond, overview.Dhan.LastFetchAt, overview.Dhan.LastFetchBars).ShouldBe((365, 4, null, null));
        overview.Aggregates.Timeframes.Select(t => t.Timeframe).ShouldBe(["5m", "10m", "15m", "30m", "1h", "1D", "1W", "1M"]);
        overview.Aggregates.Timeframes.ShouldAllBe(t => t.Bars == 0 && t.LastBucket == null);
        (overview.Aggregates.LastBuiltAt, overview.Aggregates.InstrumentsPending).ShouldBe((null, 0));
        (overview.Bars.Instruments, overview.Bars.TotalBars, overview.Bars.FirstBar, overview.Bars.LastBar, overview.Bars.DaysBehind, overview.Bars.Stale).ShouldBe((0, 0, null, null, null, false));
        overview.Bars.ExpectedSession.ShouldBe(LastSession);
        overview.RunningJob.ShouldBeNull();
        (overview.LastJobs.Csv, overview.LastJobs.Dhan, overview.LastJobs.Aggregate).ShouldBe((null, null, null));
    }

    [Fact]
    public async Task After_a_full_run_the_overview_shows_the_files_the_bars_the_aggregates_and_the_last_job_of_each_phase()
    {
        await SeedAsync("ABB");
        await SeedAsync("TCS");
        var bars = BarFixtures.Session(LastSession, 1);
        WriteCsv("ABB", bars);
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2));
        var job = await RunAsync();

        var overview = await OverviewAsync();

        (overview.Csv.FilesTotal, overview.Csv.FilesLoaded, overview.Csv.FilesPending, overview.Csv.Rows).ShouldBe((2, 2, 0, 750));
        overview.Csv.LastLoadAt!.Value.ShouldBe(Now, TimeSpan.FromMinutes(1));
        (overview.Bars.Instruments, overview.Bars.TotalBars, overview.Bars.InstrumentsBehind, overview.Bars.InstrumentsEmpty).ShouldBe((2, 750, 0, 0));
        overview.Bars.FirstBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[0].EpochSeconds);
        overview.Bars.LastBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[^1].EpochSeconds);
        overview.Bars.LastBar.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
        (overview.Bars.DaysBehind, overview.Bars.Stale).ShouldBe((0, false));
        overview.Aggregates.Timeframes.Single(t => t.Timeframe == "5m").Bars.ShouldBe(150);
        overview.Aggregates.Timeframes.Single(t => t.Timeframe == "1h").Bars.ShouldBe(14);
        overview.Aggregates.Timeframes.Single(t => t.Timeframe == "1M").Bars.ShouldBe(2);
        overview.Aggregates.Timeframes.Single(t => t.Timeframe == "1D").LastBucket!.Value.ToString("yyyy-MM-dd HH:mm zzz").ShouldBe("2026-10-09 00:00 +05:30");
        overview.Aggregates.LastBuiltAt.ShouldNotBeNull();
        (overview.LastJobs.Csv!.Id, overview.LastJobs.Dhan!.Id, overview.LastJobs.Aggregate!.Id).ShouldBe((job.Id, job.Id, job.Id));
        overview.LastJobs.Dhan.Dhan.Status.ShouldBe("skipped");
    }

    [Fact]
    public async Task Data_that_ends_before_the_last_closed_session_is_behind_by_weekday_sessions()
    {
        await SeedAsync("ABB");
        await SeedAsync("TCS");
        WriteCsv("ABB", BarFixtures.Session(new DateOnly(2026, 10, 7), 1, count: 10)); // Wednesday: Thursday and Friday are missing
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2, count: 10));
        await RunAsync(["csv"]);

        var overview = await OverviewAsync();
        var list = await InstrumentsAsync("?sort=symbol");

        (overview.Bars.DaysBehind, overview.Bars.Stale).ShouldBe((0, false)); // the newest bar of all is Friday's
        overview.Bars.InstrumentsBehind.ShouldBe(1);
        var abb = list.Items.Single(i => i.Symbol == "NSE:ABB");
        (abb.Status, abb.DaysBehind).ShouldBe(("behind", 2));
        list.Items.Single(i => i.Symbol == "NSE:TCS").Status.ShouldBe("current");
    }

    [Fact]
    public async Task The_newest_bar_older_than_the_last_closed_session_makes_the_whole_data_stale_and_a_weekend_does_not_count()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(new DateOnly(2026, 10, 7), 1, count: 10));
        await RunAsync(["csv"]);

        var overview = await OverviewAsync();
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)); // Sunday evening
        await ResignInAsync();
        var sunday = await OverviewAsync();
        Factory.Clock.Pin(new DateTimeOffset(2026, 10, 12, 10, 30, 0, TimeSpan.Zero)); // Monday 16:00 India: the session has closed
        await ResignInAsync();
        var monday = await OverviewAsync();

        (overview.Bars.DaysBehind, overview.Bars.Stale).ShouldBe((2, true));
        (sunday.Bars.ExpectedSession, sunday.Bars.DaysBehind).ShouldBe((LastSession, 2));
        (monday.Bars.ExpectedSession, monday.Bars.DaysBehind).ShouldBe((new DateOnly(2026, 10, 12), 3));
    }

    [Fact]
    public async Task Tracked_instruments_without_bars_are_counted_as_empty()
    {
        var abb = await SeedAsync("ABB");
        await SeedAsync("TCS");
        using var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { abb } }, Ct);
        tracked.EnsureSuccessStatusCode();

        var overview = await OverviewAsync();
        var list = await InstrumentsAsync();

        (overview.Bars.Instruments, overview.Bars.InstrumentsEmpty, overview.Bars.Stale).ShouldBe((0, 1, false));
        var row = list.Items.ShouldHaveSingleItem();
        (row.Symbol, row.Status, row.Bars, row.FirstBar, row.LastBar, row.Source, row.DaysBehind, row.Tracked).ShouldBe(("NSE:ABB", "empty", 0, null, null, null, null, true));
    }

    [Fact]
    public async Task The_dhan_block_follows_the_data_account_and_the_source()
    {
        var source = Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [], 0));
        Factory.Dhan.UseAccount(new DataAccountState(null, null, DataAccountStatuses.None, "No account has the primary Data role. Add or choose one under Accounts."));
        var noAccount = (await OverviewAsync()).Dhan;
        Factory.Dhan.UseAccount(new DataAccountState(Guid.NewGuid(), "Primary", DataAccountStatuses.Expired, "The Dhan sign-in of the Data account 'Primary' has expired. Reconnect it under Accounts."));
        var expired = (await OverviewAsync()).Dhan;
        Factory.Dhan.UseConnectedAccount("Primary");
        var ready = (await OverviewAsync()).Dhan;
        source.Availability = new SourceAvailability(false, "Dhan is down for maintenance");
        var unavailable = (await OverviewAsync()).Dhan;

        (noAccount.Account, noAccount.CanFetch, noAccount.ReasonCode).ShouldBe((null, false, "data.dhan_not_connected"));
        noAccount.Reason.ShouldBe("No account has the primary Data role. Add or choose one under Accounts.");
        (expired.Account!.Name, expired.Account.Status, expired.CanFetch, expired.ReasonCode).ShouldBe(("Primary", "expired", false, "data.dhan_not_connected"));
        expired.Reason.ShouldBe("The Dhan sign-in of the Data account 'Primary' has expired. Reconnect it under Accounts.");
        (ready.Account!.Status, ready.CanFetch, ready.Reason, ready.ReasonCode).ShouldBe(("connected", true, null, null));
        (unavailable.CanFetch, unavailable.Reason, unavailable.ReasonCode).ShouldBe((false, "Dhan is down for maintenance", "data.dhan_not_available"));
    }

    [Fact]
    public async Task The_real_data_account_is_read_from_the_accounts_and_the_real_source_is_ready_to_try()
    {
        // No fake at all: the real source and the real account lookup (no account has the Data role in a fresh database).
        await Factory.ResetAccountsAsync();

        var dhan = (await OverviewAsync()).Dhan;

        dhan.Account.ShouldBeNull();
        (dhan.CanFetch, dhan.ReasonCode).ShouldBe((false, "data.dhan_not_connected"));
    }
}
