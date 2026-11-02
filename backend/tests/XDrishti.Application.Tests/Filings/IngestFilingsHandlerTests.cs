using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>Spec P15-02 criteria 3, 4, 5, 7 and 10 on fakes: outcomes of one ingest run, idempotence, revisions, the disk guard and the failure paths.</summary>
public sealed class IngestFilingsHandlerTests
{
    [Fact]
    public async Task An_accepted_file_is_copied_first_and_then_stored_once_and_the_inbox_is_only_read()
    {
        var world = new FilingWorld();
        world.Add(content: "one");

        var summary = await world.RunAsync();

        summary.Stored.ShouldBe(1);
        world.Events.Select(e => e[..4]).ShouldBe(["copy", "inse"]);
        world.Store.Rows.ShouldHaveSingleItem().BroadcastTs.ShouldBe(FilingWorld.Broadcast);
        world.Store.Rows[0].IngestedAt.ShouldBe(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        world.Source.Opened.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_run_over_the_same_inbox_inserts_nothing_copies_nothing_and_opens_nothing()
    {
        var world = new FilingWorld();
        world.Add(content: "one");
        world.Add(symbol: "TCS", content: "two");
        await world.RunAsync();
        world.Source.Opened = 0;
        var copies = world.Files.Copies;

        var summary = await world.RunAsync();

        (summary.Stored, summary.Duplicate).ShouldBe((0, 2));
        world.Store.Inserts.ShouldBe(2);
        world.Files.Copies.ShouldBe(copies);
        world.Source.Opened.ShouldBe(0);
    }

    [Fact]
    public async Task The_same_bytes_under_another_name_are_one_row()
    {
        var world = new FilingWorld();
        world.Add(content: "one", name: "a.xml");
        world.Add(content: "one", name: "b.xml");

        var summary = await world.RunAsync();

        (summary.Stored, summary.Duplicate).ShouldBe((1, 1));
    }

    [Fact]
    public async Task The_same_bytes_with_another_sidecar_are_a_conflict_and_nothing_is_changed()
    {
        var world = new FilingWorld();
        world.Add(content: "one");
        await world.RunAsync();
        world.Source.Accepted.Clear();
        world.Add(symbol: "TCS", content: "one");
        world.Add(content: "one", ts: FilingWorld.Broadcast.AddHours(1));
        var before = world.Store.Rows.ToList();

        var summary = await world.RunAsync();

        summary.SidecarConflict.ShouldBe(2);
        world.Store.Rows.ShouldBe(before);
    }

    [Theory]
    [InlineData("NOSUCH", "unknown_instrument")]
    [InlineData("TWIN", "ambiguous_symbol")]
    public async Task An_unknown_or_ambiguous_symbol_is_counted_and_never_guessed(string symbol, string reason)
    {
        var world = new FilingWorld();
        world.Add(symbol: symbol);

        var summary = await world.RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { [reason] = 1 });
        world.Store.Rows.ShouldBeEmpty();
        world.Files.Copies.ShouldBe(0);
        world.Source.Opened.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_without_a_usable_time_is_stored_unavailable_with_its_reason_and_counted()
    {
        var world = new FilingWorld();
        world.Add(content: "x", state: BroadcastState.Unavailable, reason: "date_only");
        world.Add(content: "y", state: BroadcastState.Unavailable, reason: "no_offset");

        var summary = await world.RunAsync();

        (summary.Stored, summary.Unavailable).ShouldBe((2, 2));
        world.Store.Rows.ShouldAllBe(r => r.BroadcastTs == null && r.State == BroadcastState.Unavailable);
        world.Store.Rows.Select(r => r.UnavailableReason).Order().ShouldBe(["date_only", "no_offset"]);
    }

    [Fact]
    public async Task A_row_level_csv_is_deferred_to_the_loader_and_neither_opened_nor_copied_nor_stored()
    {
        var world = new FilingWorld();
        world.Add(symbol: "*", content: "csv", state: BroadcastState.RowLevelTs);

        var summary = await world.RunAsync();

        (summary.DeferredToLoader, summary.Stored).ShouldBe((1, 0));
        world.Source.Opened.ShouldBe(0);
        world.Files.Copies.ShouldBe(0);
    }

    [Fact]
    public async Task A_later_file_of_the_period_is_a_revision_an_earlier_one_is_out_of_order_and_an_equal_time_is_ambiguous()
    {
        var world = new FilingWorld();
        world.Add(content: "orig");
        world.Add(content: "restated", ts: FilingWorld.Broadcast.AddDays(37));
        world.Add(content: "early", ts: FilingWorld.Broadcast.AddDays(-5));
        world.Add(content: "same-time", ts: FilingWorld.Broadcast.AddDays(37));

        var summary = await world.RunAsync();

        (summary.Stored, summary.Revisions, summary.OutOfOrder).ShouldBe((3, 1, 1));
        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["ambiguous_order"] = 1 });
        world.Store.Rows.Single(r => r.FileBytes == 8).Supersedes.ShouldBe(world.Store.Rows.Single(r => r.FileBytes == 4).Id);
        world.Store.Rows.Single(r => r.FileBytes == 5).Supersedes.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_copy_inserts_nothing_and_is_counted_with_its_reason()
    {
        var world = new FilingWorld();
        world.Files.Fail = Result.Failure<bool>(Error.Validation("content_changed", "changed"));
        world.Add(content: "one");

        var summary = await world.RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["content_changed"] = 1 });
        world.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_source_file_is_counted_and_the_next_file_is_still_stored()
    {
        var world = new FilingWorld();
        world.Add(content: "one");
        world.Files.Throw = new IOException("gone");

        var first = await world.RunAsync();
        world.Files.Throw = null;
        var second = await world.RunAsync();

        first.Rejected.ShouldBe(new Dictionary<string, int> { ["unreadable"] = 1 });
        second.Stored.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_file_is_counted_and_does_not_block_the_others()
    {
        var world = new FilingWorld();
        world.Add(symbol: "RELIANCE", content: "one");
        world.Add(symbol: "TCS", content: "two");
        world.Store.ThrowOnInsert = new InvalidOperationException("db");

        var summary = await world.RunAsync();

        summary.Failures.ShouldBe(2);
        world.Store.Rows.ShouldBeEmpty();
        summary.Text.ShouldContain("2 failed");
    }

    [Fact]
    public async Task The_files_the_inbox_rejected_are_counted_by_reason()
    {
        var world = new FilingWorld();
        world.Source.Rejected.Add(new XDrishti.Application.Abstractions.Filings.RejectedFiling("a.xml", "no_sidecar", null));
        world.Source.Rejected.Add(new XDrishti.Application.Abstractions.Filings.RejectedFiling("b.xml", "no_sidecar", null));

        var summary = await world.RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["no_sidecar"] = 2 });
        summary.Listed.ShouldBe(2);
    }

    [Fact]
    public async Task The_size_cap_stops_the_run_before_the_next_file_and_nothing_is_stored()
    {
        var world = new FilingWorld();
        world.Files.Used = world.Settings.MaxFilesBytes + 1;
        world.Add(content: "one");

        var summary = await world.RunAsync();

        summary.Stopped.ShouldBe("disk_budget");
        world.Store.Rows.ShouldBeEmpty();
        world.Source.Opened.ShouldBe(0);
    }

    [Fact]
    public async Task Too_little_free_space_stops_the_run()
    {
        var world = new FilingWorld();
        world.Files.Free = world.Settings.MinFreeBytes - 1;
        world.Add(content: "one");

        var summary = await world.RunAsync();

        summary.Stopped.ShouldBe("disk_free");
        world.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Eighty_percent_of_the_cap_only_warns_and_the_run_goes_on()
    {
        var world = new FilingWorld();
        world.Files.Used = (long)(world.Settings.MaxFilesBytes * 0.85);
        world.Add(content: "one");

        var summary = await world.RunAsync();

        (summary.Stopped, summary.Stored).ShouldBe((null, 1));
    }

    [Fact]
    public async Task The_counts_line_names_every_count()
    {
        var world = new FilingWorld();
        world.Add(content: "one");
        world.Source.Problems.Add("inbox_permissions");

        var summary = await world.RunAsync();

        summary.Text.ShouldContain("stored 1");
        summary.Text.ShouldContain("inbox problems: inbox_permissions");
    }

    [Fact]
    public async Task A_seven_digit_time_is_cut_to_microseconds_so_a_rerun_is_a_duplicate_and_a_revision_under_a_microsecond_later_is_ambiguous()
    {
        var world = new FilingWorld();
        var seven = FilingWorld.Broadcast.AddTicks(1234567);
        world.Add(content: "one", ts: seven);
        world.Add(content: "two", ts: seven.AddTicks(1));

        var first = await world.RunAsync();
        var second = await world.RunAsync();

        world.Store.Rows.ShouldHaveSingleItem().BroadcastTs!.Value.UtcTicks.ShouldBe(seven.UtcTicks - 7);
        first.Rejected.ShouldBe(new Dictionary<string, int> { ["ambiguous_order"] = 1 });
        (second.Duplicate, second.SidecarConflict).ShouldBe((1, 0));
    }
}
