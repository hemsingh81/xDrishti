using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Review;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary>The folder's files for the review step: state, mapping, order, filters and paging, from directory entries and the log only.</summary>
public sealed class ListCsvFilesHandlerTests
{
    private readonly CsvReviewHarness _h = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<CsvFilePage> ListAsync(string? q = null, string? state = null, int? page = null, int? pageSize = null)
    {
        var result = await _h.List.HandleAsync(new ListCsvFilesQuery(new CsvFileRequest(q, state, page, pageSize)), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task A_file_is_pending_until_loaded_loaded_while_size_and_time_match_and_changed_when_either_differs()
    {
        _h.AddFile("NEW");
        _h.AddFile("SAME");
        _h.AddFile("GROWN");
        _h.AddFile("TOUCHED");
        foreach (var info in (await _h.Folder.ListAsync(Ct)).Where(f => f.Name is "SAME_1m.csv.gz" or "GROWN_1m.csv.gz" or "TOUCHED_1m.csv.gz"))
        {
            _h.Log.Add(info);
        }

        _h.Log.Loaded["GROWN_1m.csv.gz"] = _h.Log.Loaded["GROWN_1m.csv.gz"] with { Size = 1 };
        _h.Log.Loaded["TOUCHED_1m.csv.gz"] = _h.Log.Loaded["TOUCHED_1m.csv.gz"] with { ModifiedAt = _h.Log.Loaded["TOUCHED_1m.csv.gz"].ModifiedAt.AddSeconds(1) };

        var page = await ListAsync();

        page.Items.OrderBy(i => i.Symbol, StringComparer.Ordinal).Select(i => (i.Symbol, i.State)).ShouldBe([("GROWN", "changed"), ("NEW", "pending"), ("SAME", "loaded"), ("TOUCHED", "changed")]);
        page.Counts.ShouldBe(new CsvFileCounts(4, 1, 1, 2, 0));
    }

    [Fact]
    public async Task A_loaded_file_shows_its_rows_range_and_load_time_and_a_pending_one_does_not()
    {
        _h.AddFile("ABB");
        _h.AddFile("TCS");
        var first = new DateTimeOffset(2024, 4, 1, 3, 45, 0, TimeSpan.Zero);
        var last = first.AddMinutes(374);
        var loadedAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var info = (await _h.Folder.ListAsync(Ct)).Single(f => f.Name == "ABB_1m.csv.gz");
        _h.Log.Loaded[info.Name] = new LoadedCsvFile(info.Name, info.Size, info.ModifiedAt, Guid.NewGuid(), 375, 375, 0, 0, 0, loadedAt, first, last);

        var page = await ListAsync();

        var abb = page.Items.Single(i => i.Symbol == "ABB");
        (abb.Rows, abb.FirstBar, abb.LastBar, abb.LoadedAt).ShouldBe((375, first, last, loadedAt));
        var tcs = page.Items.Single(i => i.Symbol == "TCS");
        (tcs.Rows, tcs.FirstBar, tcs.LastBar, tcs.LoadedAt).ShouldBe((null, null, null, null));
    }

    [Fact]
    public async Task A_file_loaded_before_its_range_was_recorded_has_no_first_or_last_bar()
    {
        _h.AddFile("ABB");
        _h.Log.Add((await _h.Folder.ListAsync(Ct)).Single());

        var item = (await ListAsync()).Items.Single();

        (item.State, item.Rows, item.FirstBar, item.LastBar).ShouldBe(("loaded", 1, null, null));
    }

    [Fact]
    public async Task The_symbol_maps_to_an_instrument_or_is_flagged_unknown_ambiguous_or_an_invalid_name()
    {
        var abb = _h.AddFile("ABB");
        _h.Folder.Add("GONE_1m.csv", FakeCsvFolder.Csv(1));
        _h.Listed.Matches["NSE:AMBIG"] = [Guid.NewGuid(), Guid.NewGuid()];
        _h.Folder.Add("AMBIG_1m.csv", FakeCsvFolder.Csv(1));
        _h.Listed.Matches["NSE:TWICE"] = [Guid.NewGuid()];
        _h.Folder.Add("TWICE_1m.csv", FakeCsvFolder.Csv(1));
        _h.Folder.Add("TWICE_1m.csv.gz", FakeCsvFolder.Csv(1));
        _h.Folder.Add("ABB.csv", FakeCsvFolder.Csv(1));
        _h.Folder.Add("abb_1m.csv", FakeCsvFolder.Csv(1));

        var page = await ListAsync();

        page.Items.OrderBy(i => i.Name, StringComparer.Ordinal).Select(i => (i.Name, i.Mapping)).ShouldBe(
        [
            ("ABB.csv", "invalidName"),
            ("ABB_1m.csv.gz", "mapped"),
            ("AMBIG_1m.csv", "ambiguous"),
            ("GONE_1m.csv", "unknown"),
            ("TWICE_1m.csv", "ambiguous"),
            ("TWICE_1m.csv.gz", "ambiguous"),
            ("abb_1m.csv", "unknown"),
        ]);
        var mapped = page.Items.Single(i => i.Mapping == "mapped");
        mapped.Instrument.ShouldBe(new CsvInstrumentRef(abb, "NSE:ABB"));
        page.Items.Where(i => i.Mapping != "mapped").ShouldAllBe(i => i.Instrument == null);
        page.Items.Single(i => i.Name == "ABB.csv").Symbol.ShouldBeNull();
        page.Counts.Unmapped.ShouldBe(6);
    }

    [Fact]
    public async Task Files_that_are_not_csv_files_at_all_are_not_listed()
    {
        _h.AddFile("ABB");
        _h.Folder.Add("README.md", "x");
        _h.Folder.Add(".DS_Store", "x");
        _h.Folder.Add("notes.txt", "x");

        (await ListAsync()).Items.Select(i => i.Name).ShouldBe(["ABB_1m.csv.gz"]);
    }

    [Fact]
    public async Task Changed_and_pending_files_come_first_then_loaded_ones_each_by_name()
    {
        foreach (var symbol in new[] { "DDD", "AAA", "CCC", "BBB" })
        {
            _h.AddFile(symbol);
        }

        foreach (var info in (await _h.Folder.ListAsync(Ct)).Where(f => f.Name.StartsWith('A') || f.Name.StartsWith('C')))
        {
            _h.Log.Add(info);
        }

        _h.Log.Loaded["CCC_1m.csv.gz"] = _h.Log.Loaded["CCC_1m.csv.gz"] with { Size = 7 };

        (await ListAsync()).Items.Select(i => (i.Symbol, i.State)).ShouldBe([("BBB", "pending"), ("CCC", "changed"), ("DDD", "pending"), ("AAA", "loaded")]);
    }

    [Fact]
    public async Task Q_matches_the_file_name_or_symbol_ignoring_case_and_the_counts_follow_it_but_not_the_state_filter()
    {
        _h.AddFile("ABB");
        _h.AddFile("ABBOTINDIA");
        _h.AddFile("TCS");
        _h.Log.Add((await _h.Folder.ListAsync(Ct)).Single(f => f.Name == "ABB_1m.csv.gz"));

        var page = await ListAsync(q: "abb", state: "pending");

        page.Items.Select(i => i.Symbol).ShouldBe(["ABBOTINDIA"]);
        (page.Total, page.Counts.Total, page.Counts.Pending, page.Counts.Loaded).ShouldBe((1, 2, 1, 1));
        (await ListAsync(q: "_1m.csv.gz")).Total.ShouldBe(3);
    }

    [Fact]
    public async Task Paging_cuts_the_sorted_list_and_reports_the_total_after_the_filters()
    {
        foreach (var symbol in new[] { "A1", "A2", "A3", "A4", "A5" })
        {
            _h.AddFile(symbol);
        }

        var second = await ListAsync(page: 2, pageSize: 2);
        var beyond = await ListAsync(page: 9, pageSize: 2);

        second.Items.Select(i => i.Symbol).ShouldBe(["A3", "A4"]);
        (second.Page, second.PageSize, second.Total).ShouldBe((2, 2, 5));
        beyond.Items.ShouldBeEmpty();
        beyond.Total.ShouldBe(5);
    }

    [Theory]
    [InlineData(null, "bogus", null, null, "state")]
    [InlineData(null, "7", null, null, "state")]
    [InlineData(null, null, 0, null, "page")]
    [InlineData(null, null, null, 0, "pageSize")]
    [InlineData(null, null, null, 201, "pageSize")]
    [InlineData("tab\there", null, null, null, "q")]
    public async Task Bad_filters_are_refused_with_the_field(string? q, string? state, int? page, int? pageSize, string field)
    {
        var result = await _h.List.HandleAsync(new ListCsvFilesQuery(new CsvFileRequest(q, state, page, pageSize)), Ct);

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldContain(field);
    }

    [Fact]
    public async Task A_missing_folder_lists_nothing_and_says_so_and_an_unreadable_one_lists_nothing()
    {
        _h.Folder.FolderExists = false;
        var missing = await ListAsync();
        _h.Folder.FolderExists = true;
        _h.Folder.ListFailure = new UnauthorizedAccessException();
        var unreadable = await ListAsync();

        (missing.FolderExists, missing.Folder).ShouldBe((false, "/data/csv"));
        missing.Items.ShouldBeEmpty();
        unreadable.FolderExists.ShouldBeTrue();
        unreadable.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Listing_opens_no_file()
    {
        _h.AddFile("ABB");
        _h.AddFile("TCS", days: 3);

        await ListAsync();

        _h.Folder.Opened.ShouldBeEmpty();
    }
}
