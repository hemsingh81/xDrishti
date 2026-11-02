using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-04 criteria 1, 3, 8, 10 for the calendar loader, with fakes for the inbox, the instruments and the store (the store applies the real domain rule).</summary>
public sealed class LoadCalendarHandlerTests
{
    private const string Header = "symbol,period_end,meeting_date,purpose,status,intimation_ts\n";

    private const string Reliance = "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30";

    private readonly FilingWorld _world = new();

    private readonly FakeCalendarStore _store = new();

    private LoadCalendarHandler Handler() => new(_world.Source, _world.Instruments, _store, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), NullLogger<LoadCalendarHandler>.Instance);

    private async Task<RowLoadSummary> RunAsync() => (await Handler().HandleAsync(new LoadCalendarCommand(), TestContext.Current.CancellationToken)).Value;

    private FilingEntry Drop(string csv, string? name = null, string symbol = "*", FilingKind kind = FilingKind.BoardMeeting, BroadcastState state = BroadcastState.RowLevelTs)
    {
        var entry = _world.Add(symbol, kind, null, null, csv, name ?? $"meetings-{_world.Source.Accepted.Count}.csv", state);
        return entry;
    }

    [Fact]
    public async Task A_row_is_stored_once_with_its_own_intimation_time_and_a_rerun_of_the_file_inserts_nothing()
    {
        Drop(Header + Reliance);

        var first = await RunAsync();
        var second = await RunAsync();

        var row = _store.Rows.ShouldHaveSingleItem();
        (row.Filing.BroadcastTs, row.Filing.State, row.Filing.Kind, row.Filing.PeriodEnd, row.MeetingDate, row.Purpose, row.Status).ShouldBe((new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero), BroadcastState.Ok, FilingKind.BoardMeeting, new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 12), "results", "scheduled"));
        (first.Loaded, first.Duplicate, first.Files).ShouldBe((1, 0, 1));
        (second.Loaded, second.Duplicate).ShouldBe((0, 1));
        row.Filing.Id.ShouldBe(FilingId.Create(FilingWorld.Reliance, FilingKind.BoardMeeting, new DateOnly(2026, 6, 30), row.Filing.ContentSha256));
    }

    [Fact]
    public async Task The_same_row_in_two_files_is_one_row()
    {
        Drop(Header + Reliance, "a.csv");
        Drop(Header + "\n" + Reliance, "b.csv");

        var summary = await RunAsync();

        _store.Rows.Count.ShouldBe(1);
        (summary.Loaded, summary.Duplicate, summary.Files).ShouldBe((1, 1, 2));
    }

    [Fact]
    public async Task A_bad_row_never_stops_the_good_rows_and_every_rejection_is_counted_by_reason()
    {
        Drop(Header + string.Join("\n", Reliance, "RELIANCE,2026-06-30,notadate,results,scheduled,2026-08-03T11:00:00+05:30", "NOSUCH,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", "TWIN,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30"));

        var summary = await RunAsync();

        summary.Loaded.ShouldBe(2);
        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["bad_row"] = 1, ["unknown_instrument"] = 1, ["ambiguous_symbol"] = 1 });
        var file = summary.PerFile.ShouldHaveSingleItem();
        (file.FileName, file.FileReject, file.Rows, file.Loaded, file.Duplicate, file.Rejected).ShouldBe(("meetings-0.csv", null, 5, 2, 0, 3));
        file.Rejects.ShouldBe([new RowReject(3, "bad_row", "meeting_date"), new RowReject(4, "unknown_instrument", "symbol"), new RowReject(5, "ambiguous_symbol", "symbol")]);
    }

    [Fact]
    public async Task A_bad_header_refuses_the_whole_file_and_the_next_file_still_loads()
    {
        Drop("symbol,meeting_date\nRELIANCE,2026-08-12", "bad.csv");
        Drop(Header + Reliance, "good.csv");

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["bad_header"] = 1 });
        summary.Loaded.ShouldBe(1);
        summary.PerFile.Select(f => f.FileReject).ShouldBe(["bad_header", null]);
    }

    [Fact]
    public async Task A_blank_or_invalid_intimation_time_is_stored_unavailable_with_its_reason_and_counted()
    {
        Drop(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,\nTCS,2026-06-30,2026-08-12,results,scheduled,2026-08-03");

        var summary = await RunAsync();

        summary.Unavailable.ShouldBe(2);
        _store.Rows.Select(r => (r.Filing.State, r.Filing.BroadcastTs, r.Filing.UnavailableReason)).ShouldBe([(BroadcastState.Unavailable, null, "missing"), (BroadcastState.Unavailable, null, "date_only")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_reschedule_at_a_later_time_supersedes_the_stored_row_and_an_older_row_is_stored_out_of_order()
    {
        Drop(Header + Reliance);
        await RunAsync();
        Drop(Header + "RELIANCE,2026-06-30,2026-08-20,results,rescheduled,2026-08-05T10:00:00+05:30\nRELIANCE,2026-06-30,2026-08-01,results,scheduled,2026-07-20T10:00:00+05:30", "later.csv");

        var summary = await RunAsync();

        (summary.Loaded, summary.Revisions, summary.OutOfOrder, summary.Duplicate).ShouldBe((2, 1, 1, 1));
        var moved = _store.Rows.Single(r => r.Status == "rescheduled");
        moved.Filing.Supersedes.ShouldBe(_store.Rows.Single(r => r.MeetingDate == new DateOnly(2026, 8, 12)).Filing.Id);
        _store.Rows.Single(r => r.MeetingDate == new DateOnly(2026, 8, 1)).Filing.Supersedes.ShouldBeNull();
    }

    [Fact]
    public async Task Rows_with_the_same_time_and_different_content_are_refused_and_so_is_one_that_meets_a_stored_time()
    {
        Drop(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30\nRELIANCE,2026-06-30,2026-08-13,results,scheduled,2026-08-03T11:00:00+05:30\nTCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", "a.csv");
        Drop(Header + "TCS,2026-06-30,2026-07-11,results,rescheduled,2026-07-01T09:00:00+05:30", "b.csv");

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["ambiguous_order"] = 3 });
        _store.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_other_purpose_is_stored_beside_the_results_row_of_the_same_period_without_superseding_it()
    {
        Drop(Header + Reliance + "\nRELIANCE,2026-06-30,2026-08-12,other,scheduled,2026-08-04T10:00:00+05:30");

        var summary = await RunAsync();

        (summary.Loaded, summary.Revisions).ShouldBe((2, 0));
    }

    [Fact]
    public async Task A_file_with_a_named_symbol_in_its_sidecar_loads_only_that_symbols_rows()
    {
        Drop(Header + Reliance + "\nTCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", symbol: "TCS");

        var summary = await RunAsync();

        _store.Rows.ShouldHaveSingleItem().Filing.InstrumentId.ShouldBe(FilingWorld.Tcs);
        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["bad_row"] = 1 });
    }

    [Fact]
    public async Task The_line_order_and_the_file_order_do_not_change_the_stored_ids_or_the_chain_of_one_file()
    {
        var lines = new[]
        {
            "TCS,2026-06-30,2026-07-20,results,rescheduled,2026-07-10T09:00:00+05:30",
            "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30",
            "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30",
        };
        Drop(Header + string.Join("\n", lines));
        await RunAsync();
        var forward = _store.Rows.Select(r => (r.Filing.Id, r.Filing.Supersedes)).Order().ToList();

        var other = new FilingWorld();
        var store = new FakeCalendarStore();
        other.Add("*", FilingKind.BoardMeeting, null, null, Header + string.Join("\n", lines.Reverse()), "x.csv", BroadcastState.RowLevelTs);
        await new LoadCalendarHandler(other.Source, other.Instruments, store, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), NullLogger<LoadCalendarHandler>.Instance).HandleAsync(new LoadCalendarCommand(), TestContext.Current.CancellationToken);

        store.Rows.Select(r => (r.Filing.Id, r.Filing.Supersedes)).Order().ToList().ShouldBe(forward);
    }

    [Fact]
    public async Task Only_row_level_board_meeting_files_are_read_and_the_others_are_left_alone()
    {
        Drop(Header + Reliance, "r.csv", "RELIANCE", FilingKind.Results, BroadcastState.Ok);
        Drop("symbol,period_end,category,pct,pledged_pct,broadcast_ts\n", "s.csv", "*", FilingKind.Shareholding);

        var summary = await RunAsync();

        (summary.Files, _world.Source.Opened).ShouldBe((0, 0));
    }

    [Fact]
    public async Task A_file_that_cannot_be_opened_or_changed_while_read_is_counted_and_does_not_stop_the_next()
    {
        var gone = Drop(Header + Reliance, "gone.csv");
        Drop(Header + "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", "ok.csv");
        _world.Source.Content.Remove(gone.Sha256);

        var summary = await RunAsync();

        summary.Failures.ShouldBe(1);
        summary.Loaded.ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_throws_is_counted_as_a_failure_and_the_run_still_returns()
    {
        Drop(Header + Reliance);
        _store.ThrowOnInsert = new InvalidOperationException("boom");

        var summary = await RunAsync();

        (summary.Failures, summary.Loaded).ShouldBe((1, 0));
    }

    [Fact]
    public async Task The_counts_line_carries_numbers_and_reason_codes_and_nothing_else()
    {
        Drop(Header + Reliance + "\nRELIANCE,2026-06-30,bad,results,scheduled,x");

        var summary = await RunAsync();

        summary.Text.ShouldBe("calendar: files 1, loaded 1 (revisions 0, out of order 0, without an intimation time 0), duplicate 0, rejected 1 (bad_row 1), first rejects: line 3 bad_row (meeting_date)");
        summary.Text.ShouldNotContain("RELIANCE");
        summary.Text.ShouldNotContain(".csv");
    }

    [Fact]
    public async Task A_cancelled_listing_stops_the_run()
    {
        Drop(Header + Reliance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Handler().HandleAsync(new LoadCalendarCommand(), cts.Token));
    }

    [Fact]
    public async Task A_file_that_is_not_utf8_is_refused_bad_encoding_and_the_next_file_still_loads()
    {
        var bad = Drop(Header + Reliance, "latin.csv");
        _world.Source.Raw[bad.Sha256] = [.. System.Text.Encoding.UTF8.GetBytes(Header), 0xFF, 0xFE, 0x41];
        Drop(Header + "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", "ok.csv");

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["bad_encoding"] = 1 });
        (summary.Loaded, summary.PerFile[0].FileReject).ShouldBe((1, "bad_encoding"));
    }

    [Fact]
    public async Task A_row_dated_in_the_future_is_refused_bad_row_and_nothing_that_depends_on_the_clock_is_stored()
    {
        Drop(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2027-01-01T00:00:00Z");

        var summary = await RunAsync();

        summary.Loaded.ShouldBe(0);
        summary.PerFile.ShouldHaveSingleItem().Rejects.ShouldBe([new RowReject(2, "bad_row", "intimation_ts")]);
    }

    [Fact]
    public async Task The_rejects_of_a_file_are_kept_with_line_and_column_up_to_fifty()
    {
        Drop(Header + string.Join("\n", Enumerable.Range(0, 60).Select(i => $"RELIANCE,2026-06-30,bad{i},results,scheduled,2026-08-03T11:00:00+05:30")));

        var summary = await RunAsync();

        summary.Rejected["bad_row"].ShouldBe(60);
        var kept = summary.PerFile.ShouldHaveSingleItem().Rejects;
        kept.Count.ShouldBe(50);
        kept[0].ShouldBe(new RowReject(2, "bad_row", "meeting_date"));
        summary.Text.ShouldContain("line 2 bad_row (meeting_date); line 3");
    }
}
