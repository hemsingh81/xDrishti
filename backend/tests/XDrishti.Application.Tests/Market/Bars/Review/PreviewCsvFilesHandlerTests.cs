using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Review;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary>The dry run of the real reader: counts, verdicts, names, limits and the comparison against stored bars.</summary>
public sealed class PreviewCsvFilesHandlerTests
{
    private const string Header = CsvFixtures.Header;

    private readonly CsvReviewHarness _h = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<CsvPreviewResult> PreviewAsync(params string[] names)
    {
        var result = await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest(names)), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task A_good_file_reports_rows_range_days_first_rows_and_what_loading_would_do_and_writes_nothing()
    {
        var id = _h.AddFile("ABB", days: 3);
        _h.Comparison.Respond = (_, bars) => new BarComparisonResult(bars.Length - 10, 4, 5, 1);

        var preview = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single();

        (preview.Verdict, preview.Message, preview.WouldLoad, preview.State, preview.Symbol, preview.Mapping).ShouldBe(("pass", null, true, "pending", "ABB", "mapped"));
        preview.Instrument.ShouldBe(new CsvInstrumentRef(id, "NSE:ABB"));
        (preview.Rows, preview.Accepted, preview.Repaired, preview.Rejected, preview.TradingDays).ShouldBe((1125, 1125, 0, 0, 3));
        preview.FirstBar.ShouldBe(new DateTimeOffset(2024, 4, 1, 9, 15, 0, TimeSpan.FromMinutes(330)));
        preview.FirstBar!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
        preview.LastBar.ShouldBe(new DateTimeOffset(2024, 4, 3, 15, 29, 0, TimeSpan.FromMinutes(330)));
        preview.FirstRows.Count.ShouldBe(5);
        preview.FirstRows[0].ShouldBe(new CsvPreviewBar(preview.FirstBar.Value, 100, 101, 99, 100.5, 10));
        preview.FirstRows[4].Time.ShouldBe(preview.FirstBar.Value.AddMinutes(4));
        preview.Comparison.ShouldBe(new CsvPreviewComparison(1115, 4, 1, 5, 1125, false));
        _h.Comparison.Asked.ShouldBe([(id, 1125)]);

        // Nothing was written: no merge session, no file recorded, the folder only read.
        _h.Log.Loaded.ShouldBeEmpty();
        _h.Folder.Opened.ShouldBe(["ABB_1m.csv.gz"]);
    }

    [Fact]
    public async Task Repaired_and_rejected_rows_are_counted_with_reasons_and_at_most_ten_examples_without_row_values()
    {
        _h.Listed.Add("NSE:ABB");
        var good = CsvFixtures.Times(1).Take(3).ToList();
        var lines = new List<string>(good.Select(CsvFixtures.Row))
        {
            $"{good[2] + 60},105.5,100,99,100.5,,,10",   // the open above the high: repaired
        };
        for (var i = 0; i < 12; i++)
        {
            lines.Add($"{good[2] + 120 + (i * 60)},0,1,1,1,,,5"); // a price not above zero
        }

        lines.Add($"{good[0]},9,9,9,9,,,9");                     // a minute twice
        _h.Folder.Add("ABB_1m.csv", Header + "\n" + string.Join('\n', lines) + "\n");

        var preview = (await PreviewAsync("ABB_1m.csv")).Files.Single();

        (preview.Rows, preview.Accepted, preview.Repaired, preview.Rejected).ShouldBe((17, 4, 1, 13));
        preview.Rejections.ShouldBe(new Dictionary<string, long> { ["nonPositivePrice"] = 12, ["duplicateMinute"] = 1 }, ignoreOrder: true);
        preview.Examples.Count.ShouldBe(10);
        preview.Examples[0].ShouldBe("line 6: a price is not above zero");
        preview.Examples.ShouldAllBe(e => !e.Contains("105.5", StringComparison.Ordinal));
        preview.Verdict.ShouldBe("pass");
    }

    [Fact]
    public async Task A_file_with_a_shifted_clock_is_refused_with_the_message_and_is_not_compared_or_loadable()
    {
        _h.AddFile("ABB", days: 3, shiftSeconds: 19_800);

        var preview = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single();

        (preview.Verdict, preview.WouldLoad, preview.Comparison).ShouldBe(("refused", false, null));
        preview.Message!.ShouldContain("after subtracting 5 h 30 min");
        preview.Message!.ShouldContain("nothing was shifted or stored");
        (preview.Rows, preview.Accepted).ShouldBe((1125, 1125));
        _h.Comparison.Asked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("time,open\n1,2\n", "The header has no 'high', 'low', 'close', 'volume' column.")]
    [InlineData("time,open,high,low,close,volume\n1711943100000,1,2,1,1,5\n1711943160000,1,2,1,1,5\n", "look like epoch milliseconds")]
    public async Task A_file_the_load_would_fail_as_a_whole_is_refused_with_the_readers_message(string text, string reason)
    {
        _h.Listed.Add("NSE:ABB");
        _h.Folder.Add("ABB_1m.csv", text);

        var preview = (await PreviewAsync("ABB_1m.csv")).Files.Single();

        preview.Verdict.ShouldBe("refused");
        preview.Message!.ShouldContain(reason);
    }

    [Fact]
    public async Task The_limits_of_the_load_apply_to_the_preview()
    {
        _h.Listed.Add("NSE:ABB");
        _h.Folder.Add("ABB_1m.csv", CsvFixtures.Csv(CsvFixtures.Times(1)));
        _h.Settings.CsvMaxRowsPerFile = 100;

        var preview = (await PreviewAsync("ABB_1m.csv")).Files.Single();

        preview.Verdict.ShouldBe("refused");
        preview.Message!.ShouldContain("more than 100 rows (Data:Csv:MaxRowsPerFile)");
    }

    [Fact]
    public async Task An_unknown_symbol_is_read_and_reported_but_has_no_comparison_and_would_not_load()
    {
        _h.Folder.Add("GONE_1m.csv", CsvFixtures.Csv(CsvFixtures.Times(1)));

        var preview = (await PreviewAsync("GONE_1m.csv")).Files.Single();

        (preview.Verdict, preview.Mapping, preview.Instrument, preview.WouldLoad, preview.Comparison).ShouldBe(("pass", "unknown", null, false, null));
        preview.Accepted.ShouldBe(375);
        _h.Comparison.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_loaded_file_says_so_in_its_state()
    {
        _h.AddFile("ABB");
        _h.Log.Add((await _h.Folder.ListAsync(Ct)).Single());

        (await PreviewAsync("ABB_1m.csv.gz")).Files.Single().State.ShouldBe("loaded");
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("..\\ABB_1m.csv")]
    [InlineData("sub/ABB_1m.csv")]
    [InlineData("/data/csv/ABB_1m.csv")]
    [InlineData("ABB_1m.csv ")]
    [InlineData("abb_1m.csv.gz")]
    [InlineData("ABB_1m.csv\0")]
    [InlineData("")]
    [InlineData("missing_1m.csv")]
    public async Task A_name_the_folder_does_not_list_is_that_files_error_never_a_failure_and_never_opens_anything(string name)
    {
        _h.AddFile("ABB");

        var result = await PreviewAsync(name, "ABB_1m.csv.gz");

        result.Files.Count.ShouldBe(2);
        result.Files[0].Verdict.ShouldBe("error");
        result.Files[0].Message!.ShouldContain("is not a file of the CSV folder");
        (name.Length == 0 ? result.Files[0].Message! : result.Files[0].Message!.Replace(name, string.Empty, StringComparison.Ordinal)).ShouldNotContain("/data/csv"); // the folder's own path is never part of the answer
        result.Files[0].Name.Any(char.IsControl).ShouldBeFalse();
        result.Files[1].Verdict.ShouldBe("pass");
        _h.Folder.Opened.ShouldBe(["ABB_1m.csv.gz"]);
    }

    [Fact]
    public async Task A_very_long_name_is_cut_short_in_the_answer()
    {
        var result = await PreviewAsync(new string('x', 5000));

        result.Files.Single().Name.Length.ShouldBeLessThan(100);
        result.Files.Single().Message!.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public async Task A_file_with_an_invalid_name_in_the_folder_is_an_error_that_says_why()
    {
        _h.Folder.Add("ABB.csv", CsvFixtures.Csv(CsvFixtures.Times(1)));

        var preview = (await PreviewAsync("ABB.csv")).Files.Single();

        (preview.Verdict, preview.Mapping).ShouldBe(("error", "invalidName"));
        preview.Message!.ShouldContain("<SYMBOL>_1m.csv");
        _h.Folder.Opened.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_folder_is_the_error_of_every_file()
    {
        _h.Folder.FolderExists = false;

        var preview = (await PreviewAsync("ABB_1m.csv")).Files.Single();

        (preview.Verdict, preview.Message).ShouldBe(("error", "The CSV folder was not found."));
    }

    [Fact]
    public async Task A_corrupt_gzip_file_is_an_error_without_the_exception_text()
    {
        _h.Listed.Add("NSE:ABB");
        _h.Folder.Add("ABB_1m.csv.gz", string.Empty, corruptGzip: true);

        var preview = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single();

        (preview.Verdict, preview.Message).ShouldBe(("error", "The file is not a valid gzip file."));
    }

    [Fact]
    public async Task Files_are_previewed_in_the_order_asked_and_a_name_asked_twice_once()
    {
        _h.AddFile("ABB");
        _h.AddFile("TCS");

        var result = await PreviewAsync("TCS_1m.csv.gz", "ABB_1m.csv.gz", "TCS_1m.csv.gz");

        result.Files.Select(f => f.Name).ShouldBe(["TCS_1m.csv.gz", "ABB_1m.csv.gz"]);
        (result.RowsRead, result.ComparedRows).ShouldBe((750, 750));
    }

    [Fact]
    public async Task No_files_or_more_than_twenty_are_refused()
    {
        var none = await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest([])), Ct);
        var missing = await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest(null)), Ct);
        var many = await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest([.. Enumerable.Range(0, 21).Select(i => $"S{i}_1m.csv")])), Ct);
        var twenty = await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest([.. Enumerable.Range(0, 20).Select(i => $"S{i}_1m.csv")])), Ct);

        foreach (var refused in (Result<CsvPreviewResult>[])[none, missing, many])
        {
            refused.Error.Code.ShouldBe("data.invalid");
            refused.Error.Fields!.Single().Field.ShouldBe("files");
        }

        twenty.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_request_stops_reading_files_when_it_has_read_twice_the_per_file_limit()
    {
        _h.Settings.CsvMaxRowsPerFile = 300;
        for (var i = 1; i <= 6; i++)
        {
            _h.Listed.Add($"NSE:S{i}");
            _h.Folder.Add($"S{i}_1m.csv", CsvFixtures.Csv(CsvFixtures.Times(1, count: 250)));
        }

        var result = await PreviewAsync("S1_1m.csv", "S2_1m.csv", "S3_1m.csv", "S4_1m.csv", "S5_1m.csv", "S6_1m.csv");

        result.Files.Select(f => f.Verdict).ShouldBe(["pass", "pass", "pass", "error", "error", "error"]);
        result.Files[3].Message!.ShouldContain("limit of one preview request");
        result.RowsRead.ShouldBe(750); // 600 allowed, the third file started within it
        _h.Folder.Opened.ShouldNotContain("S4_1m.csv");
    }

    [Fact]
    public async Task A_request_that_runs_out_of_its_sixty_seconds_reports_the_files_not_reached_as_errors()
    {
        _h.AddFile("ABB");
        _h.AddFile("TCS");
        _h.AddFile("INFY");
        _h.Folder.OnOpen = name => { if (name == "ABB_1m.csv.gz") { _h.Clock.Advance(PreviewCsvFilesHandler.Deadline + TimeSpan.FromSeconds(1)); } };

        var result = await PreviewAsync("ABB_1m.csv.gz", "TCS_1m.csv.gz", "INFY_1m.csv.gz");

        // the file being read when the time ran out is cut short; the ones after it are not started
        result.Files.Select(f => f.Verdict).ShouldBe(["error", "error", "error"]);
        result.Files.ShouldAllBe(f => f.Message!.Contains("time limit of one preview request (60 seconds)"));
        result.Files.Select(f => f.Name).ShouldBe(["ABB_1m.csv.gz", "TCS_1m.csv.gz", "INFY_1m.csv.gz"]);
        _h.Folder.Opened.ShouldBe(["ABB_1m.csv.gz"]);
    }

    [Fact]
    public async Task Files_done_before_the_deadline_keep_their_previews()
    {
        _h.AddFile("ABB");
        _h.AddFile("TCS");
        _h.Folder.OnOpen = name => { if (name == "ABB_1m.csv.gz") { _h.Clock.Advance(TimeSpan.FromSeconds(59)); } };

        var result = await PreviewAsync("ABB_1m.csv.gz", "TCS_1m.csv.gz");

        result.Files.Select(f => f.Verdict).ShouldBe(["pass", "pass"]);
    }

    [Fact]
    public async Task The_clients_own_cancellation_is_not_turned_into_file_errors()
    {
        _h.AddFile("ABB");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await _h.Preview.HandleAsync(new PreviewCsvFilesQuery(new PreviewCsvFilesRequest(["ABB_1m.csv.gz"])), cancelled.Token));
    }

    [Fact]
    public async Task A_big_file_is_compared_on_a_spread_sample_of_batches_when_it_exceeds_its_share_and_says_so()
    {
        var id = _h.AddFile("ABB", days: 220);
        string[] names = ["ABB_1m.csv.gz", .. Enumerable.Range(0, 19).Select(i => $"gone{i}_1m.csv")]; // twenty files: a share of 50 000 stored-bar lookups each

        var result = await PreviewAsync(names);

        var big = result.Files[0];
        big.Accepted.ShouldBe(82_500);
        big.Comparison!.Sampled.ShouldBeTrue();
        big.Comparison.ComparedRows.ShouldBe(5 * 8192 + 580); // batches 0, 2, 4, 6, 8 and the last partial one (10): spread over the whole file
        big.Comparison.WouldInsert.ShouldBe(big.Comparison.ComparedRows);
        _h.Comparison.Asked.Count.ShouldBe(6);
        _h.Comparison.Asked.ShouldAllBe(a => a.InstrumentId == id);
        result.ComparedRows.ShouldBe(big.Comparison.ComparedRows);
    }

    [Fact]
    public async Task A_file_within_its_share_is_compared_in_full_and_not_flagged_sampled()
    {
        _h.AddFile("ABB", days: 100); // 37 500 rows, 5 batches

        var comparison = (await PreviewAsync("ABB_1m.csv.gz")).Files.Single().Comparison!;

        (comparison.ComparedRows, comparison.Sampled).ShouldBe((37_500, false));
        _h.Comparison.Asked.Count.ShouldBe(5);
    }
}
