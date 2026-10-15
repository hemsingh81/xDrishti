using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// Windows that dropped rows are read again after an update of the mapping (ADR 0027): a window that dropped rows for a data reason is noted; when the version of the Dhan mapping is newer than the one the account was last read with, the noted windows are read again before the run's own
/// (the identities make that safe) and the version is stored. A first real read that dropped nearly all rows because of a mapping gap fixed the same day therefore never leaves them behind. Saturday 2026-10-10 14:30 India time.
/// </summary>
public sealed class MapperVersionTests
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    private static async Task<BrokerScenario> StartAsync()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        return s;
    }

    private static List<(DateOnly From, DateOnly To)> NoteOf(BrokerScenario s) =>
        [.. s.Fx.Data.Health.Windows.Where(w => w.NeedsReread && w.Kind == SyncHealthKind.Trades).OrderBy(w => w.From).Select(w => (w.From, w.To))];

    private static SyncHealth Trades(BrokerScenario s) => s.Fx.Data.Health.Of(s.Account.Id, SyncHealthKind.Trades);

    [Fact]
    public async Task A_window_that_dropped_rows_is_noted_and_a_clean_read_of_the_same_dates_clears_the_note()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 2;

        await s.SyncOkAsync();

        NoteOf(s).ShouldBe([(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (new DateOnly(2026, 10, 1), Today)]);
        s.Fx.Data.Health.Windows.ShouldAllBe(w => w.Dropped == 2);

        s.Dhan.DroppedPerWindow = 0;
        await s.SyncOkAsync();

        NoteOf(s).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_window_that_dropped_nothing_leaves_no_note()
    {
        var s = await StartAsync();

        await s.SyncOkAsync();

        s.Fx.Data.Health.Windows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_that_has_no_symbol_counts_as_dropped_for_a_data_reason_but_an_intraday_one_does_not()
    {
        var s = await StartAsync();
        s.Dhan.SkippedPerWindow["intraday"] = 5;
        await s.SyncOkAsync();
        s.Fx.Data.Health.Windows.ShouldBeEmpty();

        s.Dhan.SkippedPerWindow.Clear();
        s.Dhan.SkippedPerWindow["no_symbol"] = 1;
        await s.SyncOkAsync();

        NoteOf(s).Count.ShouldBe(2);
    }

    [Fact]
    public async Task When_the_mapping_changes_the_noted_windows_are_read_again_before_the_runs_own_and_the_version_is_stored()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        Trades(s).MapperVersion.ShouldBe(1);
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.MapperVersion = 2; // the mappers were updated
        s.Dhan.TradeCalls.Clear();

        var view = await s.SyncOkAsync(defaultFrom: true); // incrementally from the watermark: one date

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ToList().ShouldBe(
        [
            (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            (new DateOnly(2026, 10, 1), Today), // the second window is read again whole: only a window inside the run's own dates is left to the run
            (Today, Today),
        ]);
        view.Trades.Counts.WindowsTotal.ShouldBe(3);
        NoteOf(s).ShouldBeEmpty();
        (Trades(s).MapperVersion, s.Fx.Data.Health.Of(s.Account.Id, SyncHealthKind.Ledger).MapperVersion).ShouldBe((2, 2));
    }

    [Fact]
    public async Task Once_the_version_is_stored_the_next_runs_do_not_read_them_again()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.MapperVersion = 2;
        await s.SyncOkAsync(defaultFrom: true);
        s.Dhan.DroppedPerWindow = 1; // the new mapping still drops a row of those dates
        await s.SyncOkAsync();
        NoteOf(s).Count.ShouldBe(2);
        s.Dhan.TradeCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe([(Today, Today)]); // same version: nothing is read again
    }

    [Fact]
    public async Task A_version_that_is_not_newer_reads_nothing_again_even_with_noted_windows()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.TradeCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe([(Today, Today)]);
        NoteOf(s).Count.ShouldBe(3); // the two first windows and the one just read still wait for a change of the mapping
    }

    [Fact]
    public async Task Rows_a_gap_in_the_mapping_dropped_are_stored_once_the_mapping_is_fixed()
    {
        var s = await BrokerScenario.StartAsync();
        var held = BaseTrades();
        s.Dhan.Trades.AddRange(held[..2]); // T3 is a row the old mapping could not read
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.BrokerTrades().Count.ShouldBe(2);

        s.Dhan.Trades.Add(held[2]); // the fixed mapping reads it
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.MapperVersion = 2;
        var view = await s.SyncOkAsync(defaultFrom: true);

        s.BrokerTrades().Count.ShouldBe(3);
        view.Trades.Counts.Stored.ShouldBe(1);
        NoteOf(s).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_account_the_first_version_has_not_seen_reads_again_the_dates_of_its_earlier_runs_that_dropped_rows()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Fx.Data.Health.Windows.Clear(); // windows were not noted before this version ...
        s.Fx.Data.Health.Rows[(s.Account.Id, SyncHealthKind.Trades)] = Trades(s) with { MapperVersion = 0 }; // ... and no version was stored
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.TradeCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe(
        [
            (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            (new DateOnly(2026, 10, 1), Today),
            (Today, Today),
        ]);
        Trades(s).MapperVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_reread_leaves_the_version_alone_so_the_next_run_reads_again()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.MapperVersion = 2;
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 9, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;

        var failed = await s.SyncOkAsync(defaultFrom: true);

        failed.Trades.Status.ShouldBe("failed");
        Trades(s).MapperVersion.ShouldBe(1);
        s.Dhan.TradeFailure = null;
        s.Dhan.DroppedPerWindow = 0;
        await s.SyncOkAsync(defaultFrom: true);
        Trades(s).MapperVersion.ShouldBe(2);
        NoteOf(s).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_after_the_rereads_keeps_the_version_so_a_retry_does_not_read_the_same_windows_again()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.MapperVersion = 2;
        s.Dhan.TradeFailure = (from, to, _) => from == Today && to == Today ? new BrokerFailure(BrokerFailureKind.Unavailable) : null; // the run's own window fails, after the two re-reads

        var failed = await s.SyncOkAsync(defaultFrom: true);

        failed.Trades.Status.ShouldBe("failed");
        Trades(s).MapperVersion.ShouldBe(2);
        s.Dhan.TradeFailure = null;
        s.Dhan.TradeCalls.Clear();
        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe([(Today, Today)]);
    }

    [Fact]
    public async Task A_failure_while_reading_again_leaves_the_version_alone_even_when_the_first_windows_were_read()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.MapperVersion = 2;
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null; // the second of the two re-reads fails

        var failed = await s.SyncOkAsync(defaultFrom: true);

        failed.Trades.Status.ShouldBe("failed");
        Trades(s).MapperVersion.ShouldBe(1);
    }

    [Fact]
    public async Task A_retry_after_a_failure_while_reading_again_reads_only_the_windows_that_are_not_done_even_when_they_dropped_rows_again()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.MapperVersion = 2; // the new mapping still drops a row of every window
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null; // the second of the two re-reads fails
        var failed = await s.SyncOkAsync(defaultFrom: true);
        failed.Trades.Status.ShouldBe("failed");
        failed.Trades.Counts.RereadDone.ShouldBe([new BrokerWindowSpan(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))]);
        s.Dhan.TradeFailure = null;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1)); // the retry
        s.Dhan.TradeCalls.Clear();

        var retried = await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe([(new DateOnly(2026, 10, 1), Today), (Today, Today)]); // the first window is done
        retried.Trades.Counts.WindowsTotal.ShouldBe(2);
        retried.Trades.Counts.RereadDone.Count.ShouldBe(2); // the failed attempt's window and its own
        Trades(s).MapperVersion.ShouldBe(2);
    }

    [Fact]
    public async Task A_retry_does_not_take_over_the_done_windows_of_a_failed_attempt_with_an_older_mapping()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.MapperVersion = 2;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        await s.SyncOkAsync(defaultFrom: true); // version 2: the first window is done
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.MapperVersion = 3; // the mapping changed again before the retry
        s.Dhan.TradeFailure = null;
        s.Dhan.TradeCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe(
        [
            (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
            (new DateOnly(2026, 10, 1), Today),
            (Today, Today),
        ]);
    }

    [Fact]
    public async Task A_retry_for_an_account_the_first_version_has_not_seen_reads_only_the_dates_not_done()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Fx.Data.Health.Windows.Clear();
        s.Fx.Data.Health.Rows[(s.Account.Id, SyncHealthKind.Trades)] = Trades(s) with { MapperVersion = 0 };
        s.Dhan.DroppedPerWindow = 0;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;
        await s.SyncOkAsync(defaultFrom: true); // the dates of the earlier run are read again: the first window is done, the second fails
        s.Dhan.TradeFailure = null;
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        s.Dhan.TradeCalls.Clear();

        await s.SyncOkAsync(defaultFrom: true);

        s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To)).ShouldBe([(new DateOnly(2026, 10, 1), Today), (Today, Today)]);
        Trades(s).MapperVersion.ShouldBe(1);
    }

    [Theory]
    [InlineData("2026-09-01", "2026-09-30", "2026-09-01:2026-09-10", "2026-09-11:2026-09-30")] // a start covered
    [InlineData("2026-09-01", "2026-09-30", "2026-09-10:2026-09-20", "2026-09-01:2026-09-09,2026-09-21:2026-09-30")] // the middle covered
    [InlineData("2026-09-01", "2026-09-30", "2026-08-01:2026-10-31", "")] // all covered
    [InlineData("2026-09-01", "2026-09-30", "2026-10-01:2026-10-31", "2026-09-01:2026-09-30")] // nothing covered
    [InlineData("2026-09-01", "2026-09-30", "2026-09-01:2026-09-10,2026-09-11:2026-09-30", "")] // covered by several
    public void Dates_that_are_done_are_taken_out_of_a_range(string from, string to, string done, string left)
    {
        static BrokerDateRange Range(string text) => new(DateOnly.Parse(text.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(text.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));
        var spans = done.Split(',').Select(Range).Select(r => new BrokerWindowSpan(r.From, r.To)).ToList();

        var result = BrokerHistoryPipeline.Without([Range($"{from}:{to}")], spans);

        result.ShouldBe(left.Length == 0 ? [] : left.Split(',').Select(Range));
    }

    [Fact]
    public async Task A_run_from_an_earlier_date_that_fails_part_way_keeps_the_version_so_the_windows_it_never_reached_are_read_again()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.MapperVersion = 2;
        s.Dhan.TradeFailure = (from, _, _) => from == new DateOnly(2026, 10, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null; // both noted windows lie inside this run's dates; the second is never read

        var failed = await s.SyncOkAsync(); // a manual run from the first date, not from the watermark

        failed.Trades.Status.ShouldBe("failed");
        Trades(s).MapperVersion.ShouldBe(1);
        NoteOf(s).ShouldBe([(new DateOnly(2026, 10, 1), Today)]);
        s.Dhan.TradeFailure = null;
        await s.SyncOkAsync(defaultFrom: true);
        Trades(s).MapperVersion.ShouldBe(2);
        NoteOf(s).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_from_an_earlier_date_that_succeeds_stamps_the_version_once_after_its_last_window()
    {
        var s = await StartAsync();
        s.Dhan.DroppedPerWindow = 1;
        await s.SyncOkAsync();
        s.Dhan.DroppedPerWindow = 0;
        s.Dhan.MapperVersion = 2;

        await s.SyncOkAsync();

        Trades(s).MapperVersion.ShouldBe(2);
        NoteOf(s).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_new_account_is_stamped_with_the_current_version_by_its_first_read()
    {
        var s = await StartAsync();
        s.Dhan.MapperVersion = 3;

        await s.SyncOkAsync();

        (Trades(s).MapperVersion, s.Fx.Data.Health.Of(s.Account.Id, SyncHealthKind.Ledger).MapperVersion).ShouldBe((3, 3));
        s.Dhan.TradeCalls.Where(c => c.Page == 0).Count().ShouldBe(2); // nothing extra was read
    }
}
