using System.Net;
using System.Net.Http.Json;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// Undoing the load of a CSV file against a real TimescaleDB (ADR 0017 §6, P1-07a): only the file's CSV bars inside its recorded range go (Dhan bars and other CSV bars stay), the aggregates and the coverage follow,
/// the file is pending again and loading it again restores the data, one job at a time, audited without row data. The runtime role's privileges are proved in <see cref="DataTableGrantsTests"/>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCsvUndoTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string FileName = "ABB_1m.csv.gz";

    private static readonly string[] CsvAndAggregate = ["csv", "aggregate"];

    private static readonly string[] AggregateOnly = ["aggregate"];

    private static readonly DateOnly Day1 = new(2026, 10, 8);

    private static readonly DateOnly Day2 = new(2026, 10, 9);

    private async Task<HttpResponseMessage> PostUndoAsync(string name = FileName) => await Client.PostAsync($"/api/data/csv/files/{name}/undo", Ct);

    private async Task<DataJobView> UndoAsync(string name = FileName)
    {
        using var response = await PostUndoAsync(name);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        response.Headers.Location!.ToString().ShouldBe($"/api/data/jobs/{started.Id}");
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);
        job.Status.ShouldNotBe("running");
        return job;
    }

    private async Task<CsvFilePage> FilesAsync()
    {
        using var response = await Client.GetAsync("/api/data/csv/files", Ct);
        return await response.ReadAsync<CsvFilePage>(Ct);
    }

    private async Task<string> CoverageAsync(Guid id) => await ScalarAsync<string?>(
        "SELECT bars || '/' || csv_bars || '/' || dhan_bars || '/' || (dirty_from IS NULL)::text FROM market.bar_coverage WHERE instrument_id = @id", ("id", id)) ?? "none";

    private static Bar Dhan(Bar csv) => csv with { Close = csv.High, Volume = csv.Volume + 7 };

    private async Task AssertAggregatesMatchTheStoredBarsAsync(Guid instrument)
    {
        var expected = ReferenceAggregator.Build(await StoredBarsAsync(instrument));
        var actual = await StoredAggregatesAsync(instrument);
        actual.Keys.OrderBy(k => k).ShouldBe(expected.Keys.OrderBy(k => k));
        foreach (var key in expected.Keys)
        {
            actual[key].ShouldBe(expected[key], $"{key.Timeframe} at {DateTimeOffset.FromUnixTimeSeconds(key.Start)}");
        }
    }

    [Fact]
    public async Task Undo_removes_only_the_files_csv_bars_in_its_range_and_keeps_dhan_and_other_csv_bars_and_rebuilds_the_aggregates()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        var day1 = BarFixtures.Session(Day1, 1);
        var day2 = BarFixtures.Session(Day2, 1);
        WriteCsv("ABB", [.. day1, .. day2]);
        WriteCsv("TCS", BarFixtures.Session(Day2, 5, count: 30));
        var dhanOverlay = day2.Take(5).Select(Dhan).ToList();                                   // Dhan took five minutes of the second day: a CSV load never replaces them
        foreach (var bar in dhanOverlay)
        {
            await StoreAsync(abb, bar, "dhan");
        }

        var dhanLater = BarFixtures.Session(new DateOnly(2026, 10, 12), 3, count: 3);             // newer than the file's range
        foreach (var bar in dhanLater)
        {
            await StoreAsync(abb, bar, "dhan");
        }

        var otherCsv = BarFixtures.Session(new DateOnly(2026, 10, 6), 4, count: 3);              // CSV bars of the same instrument, but before the file's range (an earlier load)
        foreach (var bar in otherCsv)
        {
            await StoreAsync(abb, bar, "csv");
        }

        (await RunAsync(["csv"])).Csv.KeptDhan.ShouldBe(5);
        (await RunAsync(AggregateOnly)).Aggregate.Full.ShouldBeTrue();
        var tcsBefore = await StoredBarsAsync(tcs);
        var tcsAggregatesBefore = await StoredAggregatesAsync(tcs);
        (await StoredBarsAsync(abb)).Count.ShouldBe(750 + 3 + 3);

        var job = await UndoAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        job.Steps.ShouldBe(["csv", "aggregate"]);
        (job.UndoCsvFile, job.InstrumentId, job.RequestedBy).ShouldBe((FileName, abb, "owner@example.invalid"));
        (job.Csv.Status, job.Csv.BarsRemoved, job.Csv.Message).ShouldBe(("succeeded", 745, "CSV load of ABB_1m.csv.gz undone, 745 bars removed"));
        (job.Aggregate.Status, job.Aggregate.Done, job.Aggregate.Total).ShouldBe(("succeeded", 1, 1));
        job.Dhan.Status.ShouldBe("notRequested");

        // The file's CSV bars are gone; every Dhan bar and the CSV bars outside the file's range are as they were.
        var left = await StoredBarsAsync(abb);
        left.ShouldBe(otherCsv.Concat(dhanOverlay).Concat(dhanLater).OrderBy(b => b.EpochSeconds));
        (await ScalarAsync<string>("SELECT string_agg(source || ':' || count, ',' ORDER BY source) FROM (SELECT source, count(*) FROM market.bars_1m WHERE instrument_id = @id GROUP BY source) s", ("id", abb))).ShouldBe("csv:3,dhan:8");

        // Another instrument is untouched, down to its aggregates.
        (await StoredBarsAsync(tcs)).ShouldBe(tcsBefore);
        (await StoredAggregatesAsync(tcs)).ShouldBe(tcsAggregatesBefore);

        // The coverage is true to what is left and nothing waits to be aggregated; the aggregates are exactly those of the remaining bars.
        (await CoverageAsync(abb)).ShouldBe("11/3/8/true");
        await AssertAggregatesMatchTheStoredBarsAsync(abb);
        (await StoredAggregatesAsync(abb)).Keys.Where(k => k.Timeframe == "1D").Count().ShouldBe(3);

        // The file is pending again and the other file stays loaded.
        var files = await FilesAsync();
        files.Items.Single(i => i.Name == FileName).State.ShouldBe("pending");
        files.Items.Single(i => i.Name == "TCS_1m.csv.gz").State.ShouldBe("loaded");
        var overview = await OverviewAsync();
        (overview.Csv.FilesLoaded, overview.Csv.FilesPending, overview.Csv.Rows).ShouldBe((1, 1, 30));
        overview.LastJobs.Csv!.Id.ShouldNotBe(job.Id); // a job about one instrument is not the last run of the CSV step
        (await ScalarAsync<long>("SELECT count(*) FROM market.csv_files WHERE undone_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task A_file_renamed_after_its_load_overlaps_its_old_row_the_list_says_so_and_the_undo_warns_naming_it_while_still_removing_the_instruments_bars()
    {
        var abb = await SeedAsync("ABB");
        var bars = new List<Bar>([.. BarFixtures.Session(Day1, 1), .. BarFixtures.Session(Day2, 1)]);
        WriteCsv("ABB", bars);
        (await RunAsync(CsvAndAggregate)).Status.ShouldBe("succeeded");
        File.Delete(CsvPath(FileName));
        WriteCsv("ABB", bars, gzip: false); // the same data under another name: ABB_1m.csv
        (await RunAsync(CsvAndAggregate)).Status.ShouldBe("succeeded");

        var files = await FilesAsync();
        var plain = files.Items.Single(i => i.Name == "ABB_1m.csv");
        var undone = await UndoAsync("ABB_1m.csv");

        plain.State.ShouldBe("loaded");
        plain.OverlapsWith.ShouldBe([FileName]);
        undone.Status.ShouldBe("succeeded", undone.Error);
        undone.Warnings.ShouldHaveSingleItem().ShouldContain(FileName);
        undone.Warnings[0].ShouldStartWith("Other loaded files of this instrument cover the same time (ABB_1m.csv.gz)");
        (await StoredBarsAsync(abb)).ShouldBeEmpty(); // the removal is by instrument and range, whichever file wrote the bars
        (await ScalarAsync<long>("SELECT count(*) FROM market.csv_files WHERE undone_at IS NULL AND file_name = @name", ("name", FileName))).ShouldBe(1); // the old row is still "loaded"
    }

    [Fact]
    public async Task Files_of_different_instruments_or_a_single_file_do_not_overlap()
    {
        await SeedAsync("ABB");
        await SeedAsync("TCS");
        WriteCsv("ABB", BarFixtures.Session(Day1, 1));
        WriteCsv("TCS", BarFixtures.Session(Day1, 2));
        (await RunAsync(CsvAndAggregate)).Status.ShouldBe("succeeded");

        var files = await FilesAsync();
        var undone = await UndoAsync();

        files.Items.ShouldAllBe(i => i.OverlapsWith != null && i.OverlapsWith.Count == 0);
        undone.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Loading_the_file_again_after_the_undo_restores_the_bars_and_the_aggregates_exactly()
    {
        var abb = await SeedAsync("ABB");
        var bars = new List<Bar>([.. BarFixtures.Session(Day1, 1), .. BarFixtures.Session(Day2, 1)]);
        WriteCsv("ABB", bars);
        (await RunAsync(CsvAndAggregate)).Status.ShouldBe("succeeded");
        var aggregatesBefore = await StoredAggregatesAsync(abb);
        var coverageBefore = await CoverageAsync(abb);

        var undone = await UndoAsync();

        (undone.Csv.BarsRemoved, undone.Status).ShouldBe((750, "succeeded"));
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
        (await StoredAggregatesAsync(abb)).ShouldBeEmpty();
        (await CoverageAsync(abb)).ShouldBe("none");
        var row = (await InstrumentsAsync()).Items.Single();
        (row.Symbol, row.Bars, row.Status, row.Tracked, row.FirstBar).ShouldBe(("NSE:ABB", 0, "empty", true, null)); // still tracked, nothing left of the data

        var reload = await RunAsync(CsvAndAggregate);

        reload.Status.ShouldBe("succeeded", reload.Error);
        (reload.Csv.FilesLoaded, reload.Csv.Rows, reload.Csv.Inserted, reload.Csv.Updated).ShouldBe((1, 750, 750, 0));
        (await StoredBarsAsync(abb)).ShouldBe(bars);
        (await StoredAggregatesAsync(abb)).ShouldBe(aggregatesBefore);
        (await CoverageAsync(abb)).ShouldBe(coverageBefore);
        (await FilesAsync()).Items.Single().State.ShouldBe("loaded");
        (await ScalarAsync<long>("SELECT count(*) FROM market.csv_files WHERE undone_at IS NOT NULL")).ShouldBe(0);
    }

    [Fact]
    public async Task The_audit_event_names_the_file_and_the_count_and_nothing_else()
    {
        await using (var cleaning = await OpenAsync())
        {
            await ExecuteAsync(cleaning, "DELETE FROM accounts.account_events WHERE event = 'CsvLoadUndone'"); // the trail outlives the tests
        }

        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(Day1, 1, count: 40));
        await RunAsync(CsvOnly);

        await UndoAsync();

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT event, detail, actor, account_id FROM accounts.account_events WHERE event = 'CsvLoadUndone'", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3)).ShouldBe(("CsvLoadUndone", "CSV load of ABB_1m.csv.gz undone, 40 bars removed", "owner@example.invalid", true));
        (await reader.ReadAsync(Ct)).ShouldBeFalse();
    }

    private static readonly string[] CsvOnly = ["csv"];

    [Fact]
    public async Task A_file_that_was_never_loaded_or_is_undone_already_or_has_no_valid_name_is_404()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(Day1, 1, count: 10));
        await RunAsync(CsvOnly);

        using var never = await PostUndoAsync("TCS_1m.csv.gz");
        await UndoAsync();
        using var twice = await PostUndoAsync();
        using var invalid = await PostUndoAsync("notes.txt");
        using var traversal = await PostUndoAsync("..%2FABB_1m.csv.gz");

        foreach (var response in new[] { never, twice, invalid })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.csv_file_not_found");
        }

        traversal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task While_a_data_job_runs_the_undo_is_409_and_removes_nothing_and_works_once_it_has_finished()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(Day1, 1, count: 10));
        await RunAsync(CsvOnly);
        var running = await StartAsync(AggregateOnly);

        using var refused = await PostUndoAsync();

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("data.job_running");
        (await StoredBarsAsync(abb)).Count.ShouldBe(10);
        (await FilesAsync()).Items.Single().State.ShouldBe("loaded");
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        (await GetJobAsync(running.Id)).Status.ShouldBe("succeeded");

        var job = await UndoAsync();

        (job.Status, job.Csv.BarsRemoved).ShouldBe(("succeeded", 10));
    }

    [Fact]
    public async Task A_file_loaded_before_its_range_was_recorded_is_undone_over_the_instruments_range_and_still_only_csv_bars()
    {
        var abb = await SeedAsync("ABB");
        var csv = BarFixtures.Session(Day1, 1, count: 20);
        WriteCsv("ABB", csv);
        var dhan = BarFixtures.Session(Day1, 1, count: 3).Select(Dhan).ToList();
        foreach (var bar in dhan)
        {
            await StoreAsync(abb, bar, "dhan");
        }

        await RunAsync(CsvAndAggregate);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.csv_files SET first_bar = NULL, last_bar = NULL");
        }

        var listed = (await FilesAsync()).Items.Single();
        var job = await UndoAsync();

        (listed.State, listed.FirstBar, listed.LastBar, listed.Rows).ShouldBe(("loaded", null, null, 20));
        job.Csv.BarsRemoved.ShouldBe(17);
        (await StoredBarsAsync(abb)).ShouldBe(dhan);
        await AssertAggregatesMatchTheStoredBarsAsync(abb);
    }

    [Fact]
    public async Task A_file_changed_on_disk_since_the_load_is_undone_by_the_range_recorded_then()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(Day1, 1, count: 20));
        await RunAsync(CsvOnly);
        WriteCsv("ABB", BarFixtures.Session(Day2, 2, count: 99), modified: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        (await FilesAsync()).Items.Single().State.ShouldBe("changed");

        var job = await UndoAsync();

        job.Csv.BarsRemoved.ShouldBe(20);
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
        (await FilesAsync()).Items.Single().State.ShouldBe("pending");
    }

    [Fact]
    public async Task Undo_works_on_compressed_chunks_and_keeps_the_dhan_bars_stored_there()
    {
        var abb = await SeedAsync("ABB");
        var csv = new List<Bar>([.. BarFixtures.Session(new DateOnly(2026, 1, 5), 1), .. BarFixtures.Session(new DateOnly(2026, 1, 6), 1), .. BarFixtures.Session(new DateOnly(2026, 3, 3), 1, count: 50)]);
        WriteCsv("ABB", csv);
        var dhan = csv.Skip(10).Take(4).Select(Dhan).Concat(csv.Skip(400).Take(2).Select(Dhan)).ToList();
        foreach (var bar in dhan)
        {
            await StoreAsync(abb, bar, "dhan");
        }

        await RunAsync(CsvAndAggregate);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "SELECT count(compress_chunk(c)) FROM show_chunks('market.bars_1m') c");
        }

        (await ScalarAsync<long>("SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'bars_1m' AND is_compressed")).ShouldBeGreaterThan(0);

        var job = await UndoAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        job.Csv.BarsRemoved.ShouldBe(csv.Count - dhan.Count);
        (await StoredBarsAsync(abb)).ShouldBe(dhan.OrderBy(b => b.EpochSeconds));
        await AssertAggregatesMatchTheStoredBarsAsync(abb);
        (await CoverageAsync(abb)).ShouldBe("6/0/6/true");
    }

    [Fact]
    public async Task The_undo_function_refuses_an_empty_range_and_missing_arguments()
    {
        await using var connection = await OpenAsync();

        var backwards = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "SELECT market.undo_csv_bars(gen_random_uuid(), '2026-10-09 04:00+00', '2026-10-08 04:00+00')"));
        var missing = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "SELECT market.undo_csv_bars(NULL, '2026-10-08 04:00+00', '2026-10-09 04:00+00')"));

        backwards.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
        missing.SqlState.ShouldBe(PostgresErrorCodes.InvalidParameterValue);
    }
}
