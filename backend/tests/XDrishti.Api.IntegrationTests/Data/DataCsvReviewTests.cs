using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The review step of the CSV load against a real TimescaleDB (ADR 0017 §6, P1-07a): the folder listing with states, the preview as a dry run of the real reader whose counts equal what a real load then reports,
/// the refusal verdicts, names that cannot leave the folder, and <c>csvFiles</c> on the refresh.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCsvReviewTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly string[] CsvOnly = ["csv"];

    private static readonly string[] AggregateOnly = ["aggregate"];

    private static readonly string[] AbbFile = ["ABB_1m.csv.gz"];

    private static readonly string[] TcsFile = ["TCS_1m.csv.gz"];

    private static readonly string[] GoneFile = ["GONE_1m.csv.gz"];

    private async Task<CsvFilePage> FilesAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/csv/files" + query, Ct);
        return await response.ReadAsync<CsvFilePage>(Ct);
    }

    private async Task<CsvPreviewResult> PreviewAsync(params string[] files)
    {
        using var response = await Client.PostJsonAsync("/api/data/csv/preview", new { files }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<CsvPreviewResult>(Ct);
    }

    /// <summary>Counts that a preview must leave exactly as they were.</summary>
    private async Task<string> WriteFingerprintAsync() => await ScalarAsync<string>(
        """
        SELECT (SELECT count(*) FROM market.bars_1m) || '/' || (SELECT coalesce(sum(volume), 0) FROM market.bars_1m) || '/' || (SELECT count(*) FROM market.bar_coverage)
            || '/' || (SELECT count(*) FROM market.csv_files) || '/' || (SELECT count(*) FROM market.data_jobs) || '/' || (SELECT count(*) FROM market.bars_agg)
            || '/' || (SELECT count(*) FROM market.tracked_instruments) || '/' || (SELECT count(*) FROM accounts.account_events)
        """);

    [Fact]
    public async Task The_listing_follows_a_file_from_pending_to_loaded_to_changed_with_its_range_and_instrument()
    {
        var abb = await SeedAsync("ABB");
        await SeedAsync("TCS");
        var bars = BarFixtures.Session(LastSession, 1, count: 50);
        WriteCsv("ABB", bars);
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2, count: 5));

        var before = await FilesAsync();
        await RunAsync(CsvOnly);
        var loaded = await FilesAsync();
        File.SetLastWriteTimeUtc(CsvPath("ABB_1m.csv.gz"), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2, count: 6), modified: new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero));
        var changed = await FilesAsync();

        before.Items.Select(i => i.State).ShouldAllBe(s => s == "pending");
        before.Counts.ShouldBe(new CsvFileCounts(2, 2, 0, 0, 0));
        loaded.Items.Select(i => i.State).ShouldAllBe(s => s == "loaded");
        var row = loaded.Items.Single(i => i.Symbol == "ABB");
        (row.Name, row.Mapping, row.Rows, row.Instrument).ShouldBe(("ABB_1m.csv.gz", "mapped", 50, new CsvInstrumentRef(abb, "NSE:ABB")));
        row.FirstBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[0].EpochSeconds);
        row.LastBar!.Value.ToUnixTimeSeconds().ShouldBe(bars[^1].EpochSeconds);
        row.LoadedAt.ShouldNotBeNull();
        row.Size.ShouldBeGreaterThan(0);
        changed.Items.OrderBy(i => i.Symbol, StringComparer.Ordinal).Select(i => (i.Symbol, i.State)).ShouldBe([("ABB", "changed"), ("TCS", "changed")]);
        changed.Items.Select(i => i.Name).ShouldBe(["ABB_1m.csv.gz", "TCS_1m.csv.gz"]);
    }

    [Fact]
    public async Task The_listing_filters_pages_and_flags_unknown_ambiguous_and_invalid_names()
    {
        await SeedAsync("ABB");
        await SeedAsync("TCS");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2, count: 5));
        WriteCsv("GONE", BarFixtures.Session(LastSession, 3, count: 5));
        File.WriteAllText(CsvPath("notes.csv"), "x");
        File.WriteAllText(CsvPath("README.md"), "x");
        await RunAsync(CsvOnly);
        WriteCsv("NEWONE", BarFixtures.Session(LastSession, 4, count: 5));

        var all = await FilesAsync();
        var pending = await FilesAsync("?state=pending");
        var searched = await FilesAsync("?q=tc");
        var page = await FilesAsync("?pageSize=2&page=2");

        all.Items.Select(i => i.Name).ShouldBe(["GONE_1m.csv.gz", "NEWONE_1m.csv.gz", "notes.csv", "ABB_1m.csv.gz", "TCS_1m.csv.gz"]); // pending first, loaded last, each by name
        all.Items.Single(i => i.Name == "GONE_1m.csv.gz").Mapping.ShouldBe("unknown");
        all.Items.Single(i => i.Name == "notes.csv").Mapping.ShouldBe("invalidName");
        all.Counts.ShouldBe(new CsvFileCounts(5, 3, 2, 0, 3));
        pending.Items.Select(i => i.Name).ShouldBe(["GONE_1m.csv.gz", "NEWONE_1m.csv.gz", "notes.csv"]);
        pending.Total.ShouldBe(3);
        searched.Items.Select(i => i.Name).ShouldBe(["TCS_1m.csv.gz"]);
        page.Items.Select(i => i.Name).ShouldBe(["notes.csv", "ABB_1m.csv.gz"]);
        (page.Total, page.Page, page.PageSize).ShouldBe((5, 2, 2));
        all.Folder.ShouldBe(Factory.CsvFolder);
    }

    [Fact]
    public async Task A_big_folder_lists_in_one_page_without_reading_the_files()
    {
        for (var i = 0; i < 214; i++)
        {
            File.WriteAllText(CsvPath($"S{i:000}_1m.csv"), "not even a csv, never read");
        }

        var page = await FilesAsync("?pageSize=200");

        (page.Total, page.Items.Count, page.Counts.Pending, page.Counts.Unmapped).ShouldBe((214, 200, 214, 214));
        (await FilesAsync("?pageSize=200&page=2")).Items.Count.ShouldBe(14);
    }

    [Theory]
    [InlineData("?state=bogus", "state")]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=201", "pageSize")]
    public async Task A_bad_listing_filter_is_400_data_invalid(string query, string field)
    {
        using var response = await Client.GetAsync("/api/data/csv/files" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        using var again = await Client.GetAsync("/api/data/csv/files" + query, Ct);
        (await again.ReadFieldErrorsAsync(Ct)).ShouldContainKey(field);
    }

    [Fact]
    public async Task The_preview_counts_are_exactly_what_a_real_load_then_reports()
    {
        var abb = await SeedAsync("ABB");
        var original = BarFixtures.Session(LastSession, 1, count: 100);
        WriteCsv("ABB", original);
        await RunAsync(CsvOnly);
        for (var i = 0; i < 10; i++)
        {
            await StoreAsync(abb, original[i] with { Close = original[i].High, Volume = original[i].Volume + 1 }, "dhan"); // Dhan took over ten minutes: a CSV bar never replaces them
        }

        var revised = BarFixtures.Session(LastSession, 1, count: 130);
        revised[20] = revised[20] with { Close = revised[20].High, Volume = 777 };      // a CSV bar that changed
        var csv = BarFixtures.Csv(
            revised,
            $"{revised[0].EpochSeconds + 200 * 60},0,1,1,1,,,5",                        // a price not above zero
            $"{revised[1].EpochSeconds},9,9,9,9,,,9",                                    // a minute twice
            $"{revised[0].EpochSeconds + 201 * 60},abc,2,1,1,,,4");                      // unparseable
        var repairedAt = revised[50];
        csv = csv.Replace(BarFixtures.Line(repairedAt), $"{repairedAt.EpochSeconds},{(repairedAt.High + 5):R},{repairedAt.High:R},{repairedAt.Low:R},{repairedAt.Close:R},,,{repairedAt.Volume}", StringComparison.Ordinal); // the open above the high: repaired
        BarFixtures.WriteFile(Factory.CsvFolder, "ABB", csv, modified: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var fingerprint = await WriteFingerprintAsync();

        var preview = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single();
        (await WriteFingerprintAsync()).ShouldBe(fingerprint);
        var job = await RunAsync(CsvOnly);

        preview.Verdict.ShouldBe("pass");
        preview.State.ShouldBe("changed");
        var counts = preview.Comparison!;
        (counts.WouldInsert, counts.WouldUpdate, counts.KeepExistingDhan, counts.Unchanged, counts.Sampled).ShouldBe((30, 2, 10, 88, false));
        (preview.Rows, preview.Accepted, preview.Repaired, preview.Rejected).ShouldBe((job.Csv.Rows, 130, job.Csv.Repaired, job.Csv.Rejected));
        (job.Csv.Inserted, job.Csv.Updated, job.Csv.Unchanged, job.Csv.KeptDhan).ShouldBe((counts.WouldInsert, counts.WouldUpdate, counts.Unchanged, counts.KeepExistingDhan));
        (preview.Repaired, preview.Rejected).ShouldBe((1, 3));
        preview.Rejections.ShouldBe(new Dictionary<string, long> { ["nonPositivePrice"] = 1, ["duplicateMinute"] = 1, ["unparseable"] = 1 }, ignoreOrder: true);
        preview.Examples.Count.ShouldBe(3);
        job.Csv.RejectionExamples.Select(e => e["ABB_1m.csv.gz ".Length..]).ShouldBe(preview.Examples, ignoreOrder: true);
    }

    [Fact]
    public async Task The_preview_reports_range_days_and_the_first_rows_of_a_new_file_and_writes_nothing()
    {
        await SeedAsync("ABB");
        var first = BarFixtures.Session(new DateOnly(2026, 10, 7), 1, count: 40);
        var days = new List<Bar>([.. first, .. BarFixtures.Session(new DateOnly(2026, 10, 8), 1, count: 40), .. BarFixtures.EveningSession(new DateOnly(2026, 10, 9), 1)]);
        WriteCsv("ABB", days);
        var fingerprint = await WriteFingerprintAsync();

        var preview = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single();

        (preview.State, preview.Mapping, preview.WouldLoad, preview.TradingDays, preview.Rows).ShouldBe(("pending", "mapped", true, 3, 140));
        preview.FirstBar!.Value.ToUnixTimeSeconds().ShouldBe(days[0].EpochSeconds);
        preview.FirstBar.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
        preview.LastBar!.Value.ToUnixTimeSeconds().ShouldBe(days[^1].EpochSeconds);
        preview.FirstRows.Select(r => (r.Time.ToUnixTimeSeconds(), r.Open, r.High, r.Low, r.Close, r.Volume)).ShouldBe(days.Take(5).Select(b => (b.EpochSeconds, b.Open, b.High, b.Low, b.Close, b.Volume)));
        preview.Comparison.ShouldBe(new CsvPreviewComparison(140, 0, 0, 0, 140, false));
        (await WriteFingerprintAsync()).ShouldBe(fingerprint);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m")).ShouldBe(0);
    }

    [Fact]
    public async Task A_shifted_file_is_refused_in_the_preview_with_the_same_message_the_load_would_fail_with()
    {
        await SeedAsync("BAD");
        var days = Enumerable.Range(0, 3).SelectMany(d => BarFixtures.Session(new DateOnly(2026, 10, 5).AddDays(d), 1)).Select(b => b with { EpochSeconds = b.EpochSeconds + 19_800 });
        BarFixtures.WriteFile(Factory.CsvFolder, "BAD", BarFixtures.Csv(days));
        var fingerprint = await WriteFingerprintAsync();

        var preview = (await PreviewAsync("BAD_1m.csv.gz")).Files.Single();
        (await WriteFingerprintAsync()).ShouldBe(fingerprint);
        var job = await RunAsync(CsvOnly);

        (preview.Verdict, preview.WouldLoad, preview.Comparison).ShouldBe(("refused", false, null));
        preview.Message!.ShouldContain("after subtracting 5 h 30 min");
        job.Csv.FailedFiles.Single().ShouldBe($"BAD_1m.csv.gz: {preview.Message}");
    }

    [Fact]
    public async Task Names_that_are_not_files_of_the_folder_are_each_files_error_and_never_read_anything_outside()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Factory.CsvFolder)!, "OUTSIDE_1m.csv"), "time,open,high,low,close,volume\n");

        var result = await PreviewAsync("../OUTSIDE_1m.csv", "..%2FOUTSIDE_1m.csv", "./ABB_1m.csv.gz", Path.Combine(Factory.CsvFolder, "ABB_1m.csv.gz"), "ABB_1m.csv.gz\0.png", "missing_1m.csv", "ABB_1m.csv.gz");

        result.Files.Select(f => f.Verdict).ShouldBe(["error", "error", "error", "error", "error", "error", "pass"]);
        result.Files.Take(6).ShouldAllBe(f => f.Message!.Contains("is not a file of the CSV folder", StringComparison.Ordinal));
        result.Files.Select(f => f.Name).ShouldAllBe(n => !n.Any(char.IsControl));
        result.Files.Take(6).ShouldAllBe(f => !f.Message!.Contains(Factory.CsvFolder, StringComparison.Ordinal) || f.Name.Contains(Factory.CsvFolder, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_files_and_more_than_twenty_files_are_400_data_invalid()
    {
        using var none = await Client.PostJsonAsync("/api/data/csv/preview", new { files = Array.Empty<string>() }, Ct);
        using var absent = await Client.PostJsonAsync("/api/data/csv/preview", new { }, Ct);
        using var many = await Client.PostJsonAsync("/api/data/csv/preview", new { files = Enumerable.Range(0, 21).Select(i => $"S{i}_1m.csv") }, Ct);
        using var twenty = await Client.PostJsonAsync("/api/data/csv/preview", new { files = Enumerable.Range(0, 20).Select(i => $"S{i}_1m.csv") }, Ct);

        foreach (var response in new[] { none, absent, many })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        }

        twenty.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_refresh_loads_only_the_named_files_and_keeps_them_on_the_job()
    {
        var abb = await SeedAsync("ABB");
        var tcs = await SeedAsync("TCS");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 10));
        WriteCsv("TCS", BarFixtures.Session(LastSession, 2, count: 10));

        using var response = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = TcsFile }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);

        started.CsvFiles.ShouldBe(["TCS_1m.csv.gz"]);
        job.CsvFiles.ShouldBe(["TCS_1m.csv.gz"]);
        job.Status.ShouldBe("succeeded", job.Error);
        (job.Csv.Total, job.Csv.FilesLoaded, job.Csv.Rows).ShouldBe((1, 1, 10));
        (await StoredBarsAsync(tcs)).Count.ShouldBe(10);
        (await StoredBarsAsync(abb)).ShouldBeEmpty();
        (await FilesAsync()).Items.ToDictionary(i => i.Symbol!, i => i.State).ShouldBe(new Dictionary<string, string> { ["ABB"] = "pending", ["TCS"] = "loaded" }, ignoreOrder: true);
        (await OverviewAsync()).Csv.FilesPending.ShouldBe(1);
    }

    [Theory]
    [InlineData("GONE_1m.csv.gz")]
    [InlineData("../ABB_1m.csv.gz")]
    [InlineData("sub/ABB_1m.csv.gz")]
    [InlineData("..\\ABB_1m.csv.gz")]
    [InlineData("abb_1m.csv.gz")]
    public async Task A_refresh_naming_a_file_the_folder_does_not_have_is_400_data_invalid_with_the_name_and_starts_no_job(string name)
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 10));

        using var response = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = (string[])["ABB_1m.csv.gz", name] }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().ShouldBe("data.invalid");
        var messages = document.RootElement.GetProperty("errors").GetProperty("csvFiles").EnumerateArray().Select(m => m.GetString()!).ToList();
        messages.ShouldHaveSingleItem().ShouldContain(name);
        Factory.DataJobs.PendingCount.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM market.data_jobs")).ShouldBe(0);
    }

    [Fact]
    public async Task A_refresh_with_an_empty_or_too_long_list_is_400_and_the_list_is_ignored_without_the_csv_step()
    {
        using var empty = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = Array.Empty<string>() }, Ct);
        using var many = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = Enumerable.Range(0, 215).Select(i => $"S{i}_1m.csv") }, Ct);
        using var ignored = await Client.PostJsonAsync("/api/data/refresh", new { steps = AggregateOnly, csvFiles = GoneFile }, Ct);

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        many.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ignored.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ignored.Content.ReadFromJsonAsync<DataJobView>(Ct))!.CsvFiles.ShouldBeNull();
        Factory.DataJobs.Clear();
    }

    [Fact]
    public async Task Named_files_that_are_unchanged_are_skipped_unless_forced()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 10));
        await RunAsync(CsvOnly);

        using var response = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = AbbFile, forceCsv = true }, Ct);
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var forced = await GetJobAsync(started.Id);
        using var again = await Client.PostJsonAsync("/api/data/refresh", new { steps = CsvOnly, csvFiles = AbbFile }, Ct);
        var second = (await again.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var skipped = await GetJobAsync(second.Id);

        (forced.Csv.FilesLoaded, forced.Csv.FilesUnchanged).ShouldBe((1, 0));
        (skipped.Csv.FilesLoaded, skipped.Csv.FilesUnchanged).ShouldBe((0, 1));
    }
}
