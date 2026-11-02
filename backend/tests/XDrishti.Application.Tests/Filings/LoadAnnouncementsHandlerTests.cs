using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-07 criteria 1 and 3 for the announcement loader: one envelope, one row and one stored text per good row, idempotent, rejects counted per reason, undated rows unavailable, logs without text.</summary>
public sealed class LoadAnnouncementsHandlerTests
{
    private const string Header = "symbol,broadcast_ts,category,subject,text\n";

    private readonly FilingWorld _world = new();

    private readonly FakeAnnouncementStore _store = new();

    private readonly CapturingLogger _logger = new();

    private LoadAnnouncementsHandler Handler() => new(_world.Source, _world.Instruments, _store, _world.Files, _world.Settings, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), _logger);

    private async Task<RowLoadSummary> RunAsync() => (await Handler().HandleAsync(new LoadAnnouncementsCommand(), TestContext.Current.CancellationToken)).Value;

    private void Drop(string csv, string? name = null, string symbol = "*", FilingKind kind = FilingKind.Announcement, BroadcastState state = BroadcastState.RowLevelTs) =>
        _world.Add(symbol, kind, null, null, csv, name ?? $"ann-{_world.Source.Accepted.Count}.csv", state);

    [Fact]
    public async Task Each_good_row_is_one_envelope_with_its_category_subject_and_its_text_in_the_store_and_a_rerun_inserts_nothing()
    {
        Drop(Header + "RELIANCE,2026-08-03T11:00:00+05:30,Results,Quarterly results,The board approved the results.\nTCS,2026-08-03T12:00:00+05:30,Trading Window,Window closes,The trading window closes.");

        var first = await RunAsync();
        var second = await RunAsync();

        _store.Rows.Count.ShouldBe(2);
        var row = _store.Rows.Single(r => r.Filing.InstrumentId == FilingWorld.Reliance);
        (row.Category, row.Headline, row.Filing.Kind, row.Filing.PeriodEnd, row.Filing.State, row.Filing.BroadcastTs).ShouldBe(("Results", "Quarterly results", FilingKind.Announcement, null, BroadcastState.Ok, new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero)));
        _world.Files.Content[row.Filing.ContentSha256].ShouldContain("The board approved the results.");
        RowCanonical.Sha256(_world.Files.Content[row.Filing.ContentSha256]).ShouldBe(row.Filing.ContentSha256);
        (first.Loaded, first.Duplicate, second.Loaded, second.Duplicate).ShouldBe((2, 0, 0, 2));
        _world.Files.Stored.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_same_row_in_two_files_is_one_row()
    {
        var row = "RELIANCE,2026-08-03T11:00:00+05:30,Results,S,T";
        Drop(Header + row, "a.csv");
        Drop(Header + "TCS,2026-08-03T11:00:00+05:30,Results,S,T\n" + row, "b.csv");

        var summary = await RunAsync();

        (summary.Loaded, summary.Duplicate).ShouldBe((2, 1));
    }

    [Fact]
    public async Task Rejects_are_counted_per_reason_and_a_bad_row_never_stops_the_good_ones()
    {
        Drop(Header + string.Join("\n",
            "RELIANCE,2026-08-03T11:00:00+05:30,Results,S,T",
            "NOSUCH,2026-08-03T11:00:00+05:30,Results,S,T",
            "TWIN,2026-08-03T11:00:00+05:30,Results,S,T",
            "TCS,2026-08-03T11:00:00+05:30,Results,S," + new string('x', 4001),
            "TCS,2026-08-03T11:00:00+05:30,,S,T",
            "TCS,2026-08-03T11:00:00+05:30,Results,S"));

        var summary = await RunAsync();

        summary.Loaded.ShouldBe(1);
        summary.Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).ShouldBe(new Dictionary<string, int> { ["ambiguous_symbol"] = 1, ["bad_row"] = 2, ["too_long"] = 1, ["unknown_instrument"] = 1 }.OrderBy(r => r.Key, StringComparer.Ordinal));
        var rejects = summary.PerFile.Single().Rejects;
        rejects.ShouldContain(r => r.Line == 5 && r.Reason == "too_long" && r.Column == "text");
        rejects.ShouldContain(r => r.Line == 6 && r.Reason == "bad_row" && r.Column == "category");
    }

    [Fact]
    public async Task A_row_with_no_usable_time_is_stored_unavailable_and_counted_never_dated()
    {
        Drop(Header + "RELIANCE,2026-08-03,Results,S,T\nTCS,,Results,S,T2");

        var summary = await RunAsync();

        summary.Unavailable.ShouldBe(2);
        _store.Rows.ShouldAllBe(r => r.Filing.State == BroadcastState.Unavailable && r.Filing.BroadcastTs == null);
        _store.Rows.Select(r => r.Filing.UnavailableReason).ShouldBe(["date_only", "missing"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_bad_header_a_bad_encoding_and_an_unreadable_file_are_counted_and_the_next_file_still_loads()
    {
        Drop("symbol,text\nA,b", "bad.csv");
        var bytes = _world.Add("*", FilingKind.Announcement, null, null, "xxx", "enc.csv", BroadcastState.RowLevelTs);
        _world.Source.Raw[bytes.Sha256] = [0xFF, 0xFE, 0xFD];
        var gone = _world.Add("*", FilingKind.Announcement, null, null, "y", "gone.csv", BroadcastState.RowLevelTs);
        _world.Source.Content.Remove(gone.Sha256);
        Drop(Header + "TCS,2026-08-03T11:00:00+05:30,Results,S,T", "ok.csv");

        var summary = await RunAsync();

        (summary.Loaded, summary.Failures).ShouldBe((1, 1));
        summary.Rejected["bad_header"].ShouldBe(1);
        summary.Rejected["bad_encoding"].ShouldBe(1);
    }

    [Fact]
    public async Task More_than_20000_rows_refuse_the_file()
    {
        Drop(Header + string.Join("\n", Enumerable.Repeat("TCS,2026-08-03T11:00:00+05:30,Results,S,T", 20_001)));

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["too_many_rows"] = 1 });
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_for_one_symbol_refuses_the_rows_of_another()
    {
        Drop(Header + "RELIANCE,2026-08-03T11:00:00+05:30,Results,S,T\nTCS,2026-08-03T11:00:00+05:30,Results,S,T", symbol: "RELIANCE");

        var summary = await RunAsync();

        (summary.Loaded, summary.Rejected["bad_row"]).ShouldBe((1, 1));
    }

    [Fact]
    public async Task An_announcement_text_or_pdf_file_is_not_a_row_level_file_and_this_loader_never_reads_it()
    {
        Drop("a pdf as text", "notice.txt", "RELIANCE", FilingKind.Announcement, BroadcastState.Ok);

        var summary = await RunAsync();

        (summary.Files, summary.Loaded, _store.Rows.Count, _world.Source.Opened).ShouldBe((0, 0, 0, 0));
    }

    [Fact]
    public async Task A_full_disk_stops_the_file_and_nothing_is_written()
    {
        _world.Files.Free = 1;
        Drop(Header + "TCS,2026-08-03T11:00:00+05:30,Results,S,T");

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["disk_free"] = 1 });
        (_store.Rows.Count, _world.Files.Copies).ShouldBe((0, 0));
    }

    [Fact]
    public async Task The_counts_line_and_every_log_message_carry_numbers_and_codes_only_never_a_subject_or_text()
    {
        Drop(Header + "RELIANCE,2026-08-03T11:00:00+05:30,SECRETCATEGORY,SECRETSUBJECT,SECRETTEXT\nTCS,2026-08-03T11:00:00+05:30,SECRETCATEGORY,SECRETSUBJECT," + new string('x', 4001), "secret-file-name.csv");

        var summary = await RunAsync();

        summary.Text.ShouldBe("announcement: files 1, loaded 1 (revisions 0, out of order 0, without an intimation time 0), duplicate 0, rejected 1 (too_long 1), first rejects: line 3 too_long (text)");
        _logger.Messages.ShouldNotBeEmpty();
        string.Join("\n", _logger.Messages).ShouldNotContain("SECRET");
        string.Join("\n", _logger.Messages).ShouldNotContain("RELIANCE");
        summary.Text.ShouldNotContain("SECRET");
    }

    /// <summary>Records the rendered log messages.</summary>
    private sealed class CapturingLogger : ILogger<LoadAnnouncementsHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
