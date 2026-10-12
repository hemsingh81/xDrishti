using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Review;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary>Loaded files of one instrument that cover the same time (a file copied under another name): found for the file list and named in the undo job's warning.</summary>
public sealed class CsvOverlapTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly Guid Abb = Guid.CreateVersion7();

    private readonly CsvReviewHarness _h = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LoadedCsvFile File(string name, Guid? instrument, int fromDay, int toDay) =>
        new(name, 1, At, instrument, 1, 1, 0, 0, 0, At, At.AddDays(fromDay), At.AddDays(toDay));

    [Fact]
    public void Files_of_one_instrument_overlap_when_their_ranges_touch_or_cross_and_not_when_they_are_apart()
    {
        LoadedCsvFile[] files =
        [
            File("a.csv", Abb, 0, 10),
            File("copy.csv", Abb, 5, 20),     // crosses a
            File("touch.csv", Abb, 10, 12),   // shares the last instant of a
            File("later.csv", Abb, 30, 40),   // apart
            File("other.csv", Guid.CreateVersion7(), 0, 10), // another instrument
        ];

        var overlaps = CsvOverlaps.All(files);

        overlaps["a.csv"].ShouldBe(["copy.csv", "touch.csv"]);
        overlaps["copy.csv"].ShouldBe(["a.csv", "touch.csv"]);
        overlaps["later.csv"].ShouldBeEmpty();
        overlaps["other.csv"].ShouldBeEmpty();
    }

    [Fact]
    public void A_file_loaded_before_its_range_was_recorded_counts_as_covering_everything()
    {
        var unknown = File("old.csv", Abb, 0, 0) with { FirstBar = null, LastBar = null };

        var overlaps = CsvOverlaps.All([unknown, File("new.csv", Abb, 400, 410)]);

        overlaps["old.csv"].ShouldBe(["new.csv"]);
        overlaps["new.csv"].ShouldBe(["old.csv"]);
    }

    [Fact]
    public async Task The_file_list_names_the_overlapping_files_of_loaded_rows_and_has_none_for_pending_ones()
    {
        var id = _h.AddFile("ABB");
        _h.AddFile("COPY");
        _h.AddFile("NEW");
        foreach (var info in await _h.Folder.ListAsync(Ct))
        {
            if (info.Name != "NEW_1m.csv.gz")
            {
                _h.Log.Loaded[info.Name] = new LoadedCsvFile(info.Name, info.Size, info.ModifiedAt, id, 375, 375, 0, 0, 0, At, At, At.AddDays(1));
            }
        }

        var items = (await _h.List.HandleAsync(new ListCsvFilesQuery(new CsvFileRequest(null, null, null, null)), Ct)).Value.Items;

        items.Single(i => i.Symbol == "ABB").OverlapsWith.ShouldBe(["COPY_1m.csv.gz"]);
        items.Single(i => i.Symbol == "COPY").OverlapsWith.ShouldBe(["ABB_1m.csv.gz"]);
        items.Single(i => i.Symbol == "NEW").OverlapsWith.ShouldBeNull();
    }

    [Fact]
    public async Task A_loaded_file_that_overlaps_nothing_has_an_empty_list()
    {
        var id = _h.AddFile("ABB");
        var info = (await _h.Folder.ListAsync(Ct)).Single();
        _h.Log.Loaded[info.Name] = new LoadedCsvFile(info.Name, info.Size, info.ModifiedAt, id, 375, 375, 0, 0, 0, At, At, At.AddDays(1));

        (await _h.List.HandleAsync(new ListCsvFilesQuery(new CsvFileRequest(null, null, null, null)), Ct)).Value.Items.Single().OverlapsWith.ShouldBeEmpty();
    }

    [Fact]
    public void The_warning_names_up_to_five_files_and_counts_the_rest()
    {
        var text = XDrishti.Application.Market.Bars.Pipeline.CsvUndoRunner.OverlapWarning([.. Enumerable.Range(1, 7).Select(i => $"f{i}.csv")]);

        text.ShouldStartWith("Other loaded files of this instrument cover the same time (f1.csv, f2.csv, f3.csv, f4.csv, f5.csv and 2 more)");
        text.ShouldContain("the undo removed their bars in the overlap too");
    }
}
