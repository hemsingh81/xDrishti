using System.Net;
using System.Net.Http.Json;
using System.Text;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>The CSV phase against a real TimescaleDB: the files of the mounted folder become 1-minute bars (ADR 0016 §4, §5).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCsvLoadTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task A_gz_file_is_loaded_into_bars_coverage_file_log_and_tracking()
    {
        var abb = await SeedAsync("ABB");
        var bars = BarFixtures.Session(LastSession, 1);
        WriteCsv("ABB", bars);

        var job = await RunAsync(["csv"]);

        job.Status.ShouldBe("succeeded", job.Error);
        job.Csv.Status.ShouldBe("succeeded");
        (job.Csv.Done, job.Csv.Total, job.Csv.FilesLoaded).ShouldBe((1, 1, 1));
        (job.Csv.Rows, job.Csv.Inserted, job.Csv.Updated, job.Csv.Unchanged).ShouldBe((375, 375, 0, 0));
        job.Dhan.Status.ShouldBe("notRequested");
        job.Aggregate.Status.ShouldBe("notRequested");
        job.CurrentPhase.ShouldBeNull();
        (await StoredBarsAsync(abb)).ShouldBe(bars);
        (await ScalarAsync<string>("SELECT string_agg(DISTINCT source, ',') FROM market.bars_1m")).ShouldBe("csv");

        var coverage = await InstrumentsAsync();
        var row = coverage.Items.ShouldHaveSingleItem();
        (row.Symbol, row.Bars, row.Source, row.Status, row.Tracked).ShouldBe(("NSE:ABB", 375, "csv", "current", true));
        row.FirstBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[0].EpochSeconds);
        row.LastBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[^1].EpochSeconds);
        (await ScalarAsync<long>("SELECT rows FROM market.csv_files WHERE file_name = 'ABB_1m.csv.gz'")).ShouldBe(375);
        job.Csv.InstrumentsTracked.ShouldBe(1);

        using var tracked = await Client.GetAsync("/api/instruments?tracked=true", Ct);
        (await tracked.ReadAsync<InstrumentPage>(Ct)).Items.ShouldHaveSingleItem().Id.ShouldBe(abb);
    }

    [Fact]
    public async Task A_plain_csv_file_with_the_columns_in_any_order_loads_like_a_gz_file()
    {
        var abb = await SeedAsync("ABB");
        var bars = BarFixtures.Session(LastSession, 2, count: 10);
        var text = new StringBuilder("Volume,CLOSE,Low,high,Open,Time,extra\r\n");
        foreach (var bar in bars)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{bar.Volume},{bar.Close:R},{bar.Low:R},{bar.High:R},{bar.Open:R},{bar.EpochSeconds},x\r\n");
        }

        BarFixtures.WriteFile(Factory.CsvFolder, "ABB", text.ToString(), gzip: false);

        var job = await RunAsync(["csv"]);

        job.Csv.Status.ShouldBe("succeeded", job.Csv.Message);
        (await StoredBarsAsync(abb)).ShouldBe(bars);
    }

    [Fact]
    public async Task Running_again_skips_unchanged_files_and_forcing_loads_them_again_without_changing_anything()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1));
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2));
        await RunAsync(["csv"]);

        var again = await RunAsync(["csv"]);
        var forced = await RunAsync(["csv"], forceCsv: true);

        (again.Csv.FilesUnchanged, again.Csv.FilesLoaded, again.Csv.Rows).ShouldBe((2, 0, 0));
        (again.Csv.Done, again.Csv.Total).ShouldBe((2, 2));
        forced.ForceCsv.ShouldBeTrue();
        (forced.Csv.FilesLoaded, forced.Csv.FilesUnchanged, forced.Csv.Rows).ShouldBe((2, 0, 750));
        (forced.Csv.Inserted, forced.Csv.Updated, forced.Csv.Unchanged).ShouldBe((0, 0, 750));
        (await StoredBarsAsync(abb)).Count.ShouldBe(375);
        (await StoredBarsAsync(tcs)).Count.ShouldBe(375);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m")).ShouldBe(750);
    }

    [Fact]
    public async Task A_changed_file_updates_the_changed_minutes_and_adds_the_new_ones()
    {
        var abb = await SeedAsync("ABB");
        var first = BarFixtures.Session(LastSession, 1, count: 100);
        WriteCsv("ABB", first);
        await RunAsync(["csv"]);

        var revised = BarFixtures.Session(LastSession, 1, count: 120);
        revised[5] = revised[5] with { Close = revised[5].High, Volume = 777 };
        WriteCsv("ABB", revised, modified: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var job = await RunAsync(["csv"]);

        (job.Csv.Rows, job.Csv.Inserted, job.Csv.Updated, job.Csv.Unchanged).ShouldBe((120, 20, 1, 99));
        (await StoredBarsAsync(abb)).ShouldBe(revised);
    }

    [Fact]
    public async Task Rows_that_break_the_rules_are_repaired_or_rejected_and_counted_with_examples()
    {
        var abb = await SeedAsync("ABB");
        var good = BarFixtures.Session(LastSession, 1, count: 4);
        var start = good[0].EpochSeconds;
        var repairedOpen = new Bar(start + 4 * 60, 105.5, 100.0, 99.0, 100.5, 10); // the open is above the high: repaired
        var zeroVolume = new Bar(start + 5 * 60, 100.0, 101.0, 99.0, 100.5, 0);   // kept
        var csv = BarFixtures.Csv(
            [.. good, repairedOpen, zeroVolume],
            $"{start + 6 * 60},0,1,1,1,,,5",                 // price not above zero
            $"{start + 7 * 60},1,2,1,1,,,-4",                // negative volume
            $"{start + 8 * 60 + 7},1,2,1,1,,,4",             // not a whole minute
            $"{start + 9 * 60},abc,2,1,1,,,4",               // unparseable
            $"{start + 10 * 60},1,2,1,1,,",                  // fewer values than the header
            $"{start + 11 * 60},1,2,1,1,,,",                 // empty volume
            $"{good[1].EpochSeconds},9,9,9,9,,,9",           // the same minute twice
            "1711943100000,1,2,1,1,,,4",                     // milliseconds, not seconds
            "",
            $"{start + 12 * 60},NaN,2,1,1,,,4");
        BarFixtures.WriteFile(Factory.CsvFolder, "ABB", csv);

        var job = await RunAsync(["csv"]);

        job.Csv.Status.ShouldBe("succeeded", job.Csv.Message);
        (job.Csv.Rows, job.Csv.Inserted, job.Csv.Repaired, job.Csv.Rejected).ShouldBe((15, 6, 1, 9));
        job.Csv.Rejections.ShouldBe(new Dictionary<string, long>
        {
            ["nonPositivePrice"] = 1,
            ["negativeVolume"] = 1,
            ["notWholeMinute"] = 1,
            ["unparseable"] = 3,
            ["duplicateMinute"] = 1,
            ["timeOutOfRange"] = 1,
            ["notANumber"] = 1,
        }, ignoreOrder: true);
        job.Csv.RejectionExamples.Count.ShouldBe(9);
        job.Csv.RejectionExamples[0].ShouldBe("ABB_1m.csv.gz line 8: a price is not above zero");
        var stored = await StoredBarsAsync(abb);
        stored.Count.ShouldBe(6);
        stored[4].ShouldBe(repairedOpen with { High = 105.5 });
        stored[5].Volume.ShouldBe(0);
        stored[1].ShouldBe(good[1]); // the first of the two copies is kept
        (await ScalarAsync<long>("SELECT repaired FROM market.csv_files")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT rejected FROM market.csv_files")).ShouldBe(9);
    }

    [Fact]
    public async Task Csv_never_overwrites_a_dhan_bar_but_still_fills_the_other_minutes()
    {
        var abb = await SeedAsync("ABB");
        var bars = BarFixtures.Session(LastSession, 1, count: 10);
        var dhanBar = bars[3] with { Open = 50, High = 60, Low = 40, Close = 55, Volume = 1 };
        await StoreAsync(abb, dhanBar, "dhan");
        WriteCsv("ABB", bars);

        var job = await RunAsync(["csv"]);

        (job.Csv.Rows, job.Csv.Inserted, job.Csv.KeptDhan, job.Csv.Updated).ShouldBe((10, 9, 1, 0));
        var stored = await StoredBarsAsync(abb);
        stored[3].ShouldBe(dhanBar);
        stored.Count.ShouldBe(10);
        (await ScalarAsync<string>("SELECT source FROM market.bars_1m WHERE ts = to_timestamp(@ts)", ("ts", (double)dhanBar.EpochSeconds))).ShouldBe("dhan");
        var row = (await InstrumentsAsync()).Items.Single();
        row.Source.ShouldBe("mixed");
    }

    [Fact]
    public async Task Unknown_ambiguous_retired_and_unlisted_symbols_are_listed_and_skipped()
    {
        var abb = await SeedAsync("ABB");
        var etf = await SeedAsync("GOLDBEES", "etf");
        await SeedAsync("OLD", retired: true);
        await SeedAsync("NIFTY", "index", canonical: "NSE:NIFTY");
        await SeedAsync("DUP");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        WriteCsv("GOLDBEES", BarFixtures.Session(LastSession, 2, count: 5));
        WriteCsv("OLD", BarFixtures.Session(LastSession, 3, count: 5));
        WriteCsv("NIFTY", BarFixtures.Session(LastSession, 3, count: 5));
        WriteCsv("NOSUCH", BarFixtures.Session(LastSession, 3, count: 5));
        WriteCsv("DUP", BarFixtures.Session(LastSession, 4, count: 5));
        WriteCsv("DUP", BarFixtures.Session(LastSession, 5, count: 5), gzip: false);
        File.WriteAllText(CsvPath("notes.txt"), "not a data file");
        File.WriteAllText(CsvPath("ABB_5m.csv"), "time,open");

        var job = await RunAsync(["csv"]);

        job.Csv.Status.ShouldBe("succeeded");
        (job.Csv.Total, job.Csv.FilesLoaded, job.Csv.FilesSkipped).ShouldBe((7, 2, 5));
        job.Csv.UnknownSymbols.ShouldBe(["NSE:NIFTY", "NSE:NOSUCH", "NSE:OLD"], ignoreOrder: true);
        job.Csv.AmbiguousSymbols.ShouldBe(["NSE:DUP", "NSE:DUP"]);
        (await StoredBarsAsync(abb)).Count.ShouldBe(5);
        (await StoredBarsAsync(etf)).Count.ShouldBe(5);
        (await ScalarAsync<long>("SELECT count(DISTINCT instrument_id) FROM market.bars_1m")).ShouldBe(2);
        (await OverviewAsync()).Csv.FilesPending.ShouldBe(5);
    }

    [Fact]
    public async Task A_file_that_cannot_be_used_fails_alone_stores_nothing_and_the_other_files_still_load()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        var bars = BarFixtures.Session(LastSession, 1, count: 20);
        WriteCsv("ABB", bars, extraLines: [new string('x', 5000)]);                       // a line that is no bar, after valid rows
        BarFixtures.WriteFile(Factory.CsvFolder, "TCS", "time,open,high,low,close\n1,2,3,4,5\n");  // no volume column
        await SeedAsync("BAD");
        File.WriteAllBytes(CsvPath("BAD_1m.csv.gz"), [1, 2, 3, 4, 5, 6, 7, 8]);            // not gzip
        await SeedAsync("OK");
        WriteCsv("OK", BarFixtures.Session(LastSession, 9, count: 5));

        var job = await RunAsync(["csv", "aggregate"]);

        job.Status.ShouldBe("failed");
        job.Csv.Status.ShouldBe("failed");
        (job.Csv.FilesFailed, job.Csv.FilesLoaded).ShouldBe((3, 1));
        job.Csv.FailedFiles.Count.ShouldBe(3);
        job.Csv.FailedFiles.ShouldContain(f => f.StartsWith("ABB_1m.csv.gz: Line 22 is longer than", StringComparison.Ordinal));
        job.Csv.FailedFiles.ShouldContain("TCS_1m.csv.gz: The header has no 'volume' column.");
        job.Csv.FailedFiles.ShouldContain("BAD_1m.csv.gz: the file is not a valid gzip file");
        job.Error.ShouldStartWith("3 of 4 files could not be loaded");
        job.Aggregate.Status.ShouldBe("succeeded"); // the phases after a failed one still run
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
        (await StoredBarsAsync(tcs)).ShouldBeEmpty();
        (await ScalarAsync<long>("SELECT count(*) FROM market.csv_files")).ShouldBe(1);
        (await OverviewAsync()).Csv.FilesPending.ShouldBe(3);
    }

    [Fact]
    public async Task Without_an_instrument_list_a_csv_job_is_refused_but_aggregates_alone_are_not()
    {
        using var refused = await PostRefreshAsync(["csv"]);
        using var all = await PostRefreshAsync();
        using var aggregateOnly = await PostRefreshAsync(["aggregate"]);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("data.master_missing");
        all.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        aggregateOnly.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_missing_folder_skips_the_phase_with_its_reason_and_an_empty_folder_loads_nothing()
    {
        await SeedAsync("ABB");
        var empty = await RunAsync(["csv"]);
        Directory.Delete(Factory.CsvFolder);
        try
        {
            var missing = await RunAsync(["csv"]);

            missing.Status.ShouldBe("succeeded");
            missing.Csv.Status.ShouldBe("skipped");
            missing.Csv.Code.ShouldBe("data.csv_folder_missing");
            missing.Csv.Message!.ShouldContain(Factory.CsvFolder);
            (await OverviewAsync()).Csv.FolderExists.ShouldBeFalse();
        }
        finally
        {
            Directory.CreateDirectory(Factory.CsvFolder);
        }

        empty.Csv.Status.ShouldBe("succeeded");
        (empty.Csv.Total, empty.Csv.Rows).ShouldBe((0, 0));
        empty.Csv.Message!.ShouldContain("no <SYMBOL>_1m.csv files");
    }

    [Fact]
    public async Task Unchanged_files_are_not_tracked_again_after_the_owner_untracked_the_instrument()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        await RunAsync(["csv"]);
        using var untrack = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/instruments/tracked", UriKind.Relative)) { Content = JsonContent.Create(new { instrumentIds = new[] { abb } }) };
        (await Client.SendAsync(untrack, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var job = await RunAsync(["csv"]);

        job.Csv.FilesUnchanged.ShouldBe(1);
        job.Csv.InstrumentsTracked.ShouldBe(0);
        (await ScalarAsync<bool>("SELECT active FROM market.tracked_instruments WHERE instrument_id = @id", ("id", abb))).ShouldBeFalse();
    }
}
