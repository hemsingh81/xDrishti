using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Tests.OptionQuality;

public sealed class OptionArchiveQualityRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static ChainRequestSummary Requests(Guid underlying, int planned, int ok) =>
        new(underlying, planned, ok, new Dictionary<string, int> { ["ok"] = ok, ["timeout"] = planned - ok });

    private static IEnumerable<OptionQualityRow> Of(QualityWorld world, string source, string code) =>
        world.Store.Rows.Select(r => r.Row).Where(r => r.Source == source && r.Check.Code == code);

    [Fact]
    public async Task An_empty_archive_writes_no_result_and_still_records_the_run()
    {
        var world = new QualityWorld();

        var summary = await world.Runner().RunAsync("daily", Ct);

        summary.Sessions.ShouldBe(0);
        summary.Results.ShouldBe(0);
        world.Store.Rows.ShouldBeEmpty();
        world.Store.Finished.ShouldHaveSingleItem().Trigger.ShouldBe("daily");
    }

    [Fact]
    public async Task A_recorded_session_with_no_snapshot_rows_is_unavailable_in_every_data_check_and_never_pass()
    {
        var world = new QualityWorld();
        world.Archive.PendingChain.Add(Monday);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 100, 100)];

        await world.Runner().RunAsync("daily", Ct);

        var checks = world.Store.Rows.Select(r => r.Row.Check).ToDictionary(c => c.Code);
        checks["coverage"].Status.ShouldBe(QualityStatus.Pass);
        checks["parity"].Status.ShouldBe(QualityStatus.Unavailable);
        checks["parity"].Reason.ShouldBe("no_two_sided_quote");
        checks["greeks"].Status.ShouldBe(QualityStatus.Unavailable);
        checks["oi_volume"].Status.ShouldBe(QualityStatus.Unavailable);
        QualityStatuses.Worst(checks.Values.Select(c => c.Status)).ShouldBe(QualityStatus.Unavailable);
    }

    [Fact]
    public async Task One_session_gets_four_chain_checks_per_underlying_and_the_gaps_show_in_coverage()
    {
        var world = new QualityWorld();
        world.Archive.PendingChain.Add(Monday);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 100, 100), Requests(QualityWorld.BankNifty, 100, 60)];

        var summary = await world.Runner().RunAsync("daily", Ct);

        summary.Results.ShouldBe(8);
        Of(world, "chain", "coverage").Single(r => r.UnderlyingId == QualityWorld.BankNifty).Check.Status.ShouldBe(QualityStatus.Fail);
        Of(world, "chain", "coverage").Single(r => r.UnderlyingId == QualityWorld.Nifty).Check.Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public async Task The_lot_size_in_force_on_the_session_decides_the_ten_lot_floor_of_an_oi_jump()
    {
        var world = new QualityWorld();
        world.Archive.PendingChain.Add(Monday);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 10, 10)];
        var open = new DateTimeOffset(2026, 10, 12, 9, 20, 0, TimeSpan.FromMinutes(330));
        world.Archive.Points[(QualityWorld.Nifty, Monday)] =
        [
            new ChainPoint(open, Monday, 10_000, true, 7, 10_000, 90, 110, 1_000, 5, null, null, null, null, 0),
            new ChainPoint(open.AddMinutes(5), Monday, 10_000, true, 7, 10_000, 90, 110, 2_001, 6, null, null, null, null, 0),
        ];
        world.Reference.Lots.Add(new LotSizeEntry(1, QualityWorld.Nifty, new DateOnly(2025, 12, 30), 100, "secondary", false));

        await world.Runner().RunAsync("daily", Ct);

        Of(world, "chain", "oi_volume").Single().Check.Detail.ShouldNotBeNull().ShouldContain("\"oi_jumps\":1");
    }

    [Fact]
    public async Task Trading_days_with_no_session_row_are_reported_as_not_recorded_once()
    {
        var world = new QualityWorld(new DateTimeOffset(2026, 10, 14, 16, 15, 0, TimeSpan.FromMinutes(330)));
        world.Archive.First = Monday;
        world.Archive.RecordedSessions.Add(Monday);
        world.Archive.CheckedSessions.Add(Monday);
        world.Archive.Underlyings.Add(QualityWorld.Nifty);

        await world.Runner().RunAsync("daily", Ct);

        var days = Of(world, "chain", "coverage").Select(r => r.SessionDate).Order().ToList();
        days.ShouldBe([new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 14)]); // the Tuesday and the Wednesday; Monday is recorded
        Of(world, "chain", "coverage").ShouldAllBe(r => r.Check.Status == QualityStatus.Unavailable && r.Check.Reason == "not_recorded");
    }

    [Fact]
    public async Task A_weekend_is_not_a_missing_session()
    {
        var world = new QualityWorld(new DateTimeOffset(2026, 10, 12, 16, 15, 0, TimeSpan.FromMinutes(330)));
        world.Archive.First = new DateOnly(2026, 10, 9);
        world.Archive.RecordedSessions.Add(new DateOnly(2026, 10, 9));
        world.Archive.CheckedSessions.Add(new DateOnly(2026, 10, 9));
        world.Archive.Underlyings.Add(QualityWorld.Nifty);

        await world.Runner().RunAsync("daily", Ct);

        Of(world, "chain", "coverage").Select(r => r.SessionDate).ShouldBe([Monday]);
    }

    [Fact]
    public async Task Without_a_single_recorded_session_nothing_is_said_about_the_chain()
    {
        var world = new QualityWorld();
        world.Archive.Underlyings.Add(QualityWorld.Nifty);

        await world.Runner().RunAsync("daily", Ct);

        world.Store.Rows.ShouldBeEmpty();
    }

    private static RollingWindow Window(Guid underlying, char flag, string outcome, DateOnly from) =>
        new(underlying, flag, 1, 0, "CE", 5, from, from.AddDays(30), outcome, $"{underlying}:{flag}1:0:CE:5");

    [Fact]
    public async Task An_empty_weekly_window_of_banknifty_after_the_cut_is_out_of_era_and_an_empty_one_of_nifty_is_a_fail()
    {
        var world = new QualityWorld();
        world.Reference.Epochs.Add(new MarketEpoch("weekly_index_one_per_exchange", new DateOnly(2024, 11, 20), "one weekly", null, false));
        var from = new DateOnly(2025, 1, 1);
        world.Archive.PendingRolling.Add(Window(QualityWorld.BankNifty, 'W', "empty", from));
        world.Archive.PendingRolling.Add(Window(QualityWorld.Nifty, 'W', "empty", from));

        await world.Runner().RunAsync("daily", Ct);

        var coverage = Of(world, "rolling", "coverage").ToList();
        coverage.Where(r => r.UnderlyingId == QualityWorld.BankNifty).ShouldAllBe(r => r.Check.Status == QualityStatus.Unavailable && r.Check.Reason == "out_of_era");
        coverage.Where(r => r.UnderlyingId == QualityWorld.Nifty).ShouldAllBe(r => r.Check.Status == QualityStatus.Fail);
        coverage.Where(r => r.UnderlyingId == QualityWorld.Nifty).Select(r => r.SessionDate).ShouldAllBe(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
    }

    [Fact]
    public async Task Rolling_bars_are_counted_per_india_day_so_a_bar_just_after_india_midnight_belongs_to_the_next_day()
    {
        var world = new QualityWorld();
        var window = Window(QualityWorld.Nifty, 'M', "ok", new DateOnly(2021, 12, 6));
        world.Archive.PendingRolling.Add(window);
        var open = new DateTimeOffset(2021, 12, 6, 9, 15, 0, TimeSpan.FromMinutes(330));
        var bars = Enumerable.Range(0, 75).Select(i => new RollingPoint(open.AddMinutes(i * 5), 1_750_000, 1_750_000, 1_750_000)).ToList();
        bars.Add(new RollingPoint(new DateTimeOffset(2021, 12, 6, 18, 35, 0, TimeSpan.Zero), 1_750_000, 1_750_000, null)); // 00:05 on 7 December in India
        world.Archive.Bars[window.FetchKey] = bars;

        await world.Runner().RunAsync("daily", Ct);

        var coverage = Of(world, "rolling", "coverage").ToDictionary(r => r.SessionDate, r => r.Check);
        coverage[new DateOnly(2021, 12, 6)].Status.ShouldBe(QualityStatus.Pass);
        coverage[new DateOnly(2021, 12, 7)].Status.ShouldBe(QualityStatus.Fail); // one bar of 75
        coverage[new DateOnly(2021, 12, 6)].N.ShouldBe(75);
        world.Store.Rows.Where(r => r.Row.Source == "rolling").ShouldAllBe(r => r.Row.FetchKey == window.FetchKey);
    }

    [Fact]
    public async Task An_unknown_strike_step_makes_stitching_unavailable_and_usable_false()
    {
        var world = new QualityWorld { };
        world.Reference.Step = null;
        var window = Window(QualityWorld.Nifty, 'M', "ok", new DateOnly(2021, 12, 6));
        world.Archive.PendingRolling.Add(window);
        var open = new DateTimeOffset(2021, 12, 6, 9, 15, 0, TimeSpan.FromMinutes(330));
        world.Archive.Bars[window.FetchKey] = [.. Enumerable.Range(0, 75).Select(i => new RollingPoint(open.AddMinutes(i * 5), 1_750_000, 1_750_000, 1_750_000))];

        await world.Runner().RunAsync("daily", Ct);

        var day = world.Store.Rows.Select(r => r.Row).Where(r => r.SessionDate == new DateOnly(2021, 12, 6) && r.Source == "rolling").Select(r => r.Check).ToList();
        day.Single(c => c.Code == "stitching").Reason.ShouldBe("no_strike_step");
        SessionUsability.IsUsable("rolling", day).ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_stops_between_two_sessions_when_its_time_is_used_up_and_leaves_the_rest_for_the_next_run()
    {
        var world = new QualityWorld();
        world.Archive.PendingChain.AddRange([Monday, Monday.AddDays(1)]);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 10, 10)];
        world.Archive.Summaries[Monday.AddDays(1)] = [Requests(QualityWorld.Nifty, 10, 10)];
        world.Store.AfterAdd = () => world.Clock.Advance(TimeSpan.FromMinutes(31));

        var summary = await world.Runner().RunAsync("manual", Ct);

        summary.Stopped.ShouldBeTrue();
        summary.Sessions.ShouldBe(1);
        world.Store.Rows.Select(r => r.Row.SessionDate).Distinct().ShouldBe([Monday]);
        world.Store.Finished.ShouldHaveSingleItem().Trigger.ShouldBe("manual");
        world.Store.Finished[0].Note.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_limits_of_a_run_bound_how_many_sessions_and_windows_it_takes()
    {
        var world = new QualityWorld();
        world.Settings.MaxChainSessionsPerRun = 1;
        world.Settings.MaxRollingWindowsPerRun = 1;
        world.Archive.PendingChain.AddRange([Monday, Monday.AddDays(1)]);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 10, 10)];
        world.Archive.PendingRolling.Add(Window(QualityWorld.Nifty, 'M', "ok", new DateOnly(2021, 12, 6)));
        world.Archive.PendingRolling.Add(Window(QualityWorld.BankNifty, 'M', "ok", new DateOnly(2021, 12, 6)));

        await world.Runner().RunAsync("daily", Ct);

        world.Store.Rows.Where(r => r.Row.Source == "chain").Select(r => r.Row.SessionDate).Distinct().ShouldBe([Monday]);
        world.Store.Rows.Where(r => r.Row.Source == "rolling").Select(r => r.Row.UnderlyingId).Distinct().ShouldBe([QualityWorld.Nifty]);
    }

    [Fact]
    public async Task A_session_that_always_throws_is_logged_and_noted_and_does_not_block_the_later_ones()
    {
        var world = new QualityWorld();
        world.Archive.PendingChain.AddRange([Monday, Monday.AddDays(1)]);
        world.Archive.Summaries[Monday] = [Requests(QualityWorld.Nifty, 10, 10)];
        world.Archive.Summaries[Monday.AddDays(1)] = [Requests(QualityWorld.Nifty, 10, 10)];
        world.Archive.ThrowOnChainPoints = new InvalidOperationException("database down");
        world.Archive.ThrowOnDays.Add(Monday);

        var summary = await world.Runner().RunAsync("daily", Ct);

        summary.Failures.ShouldBe(1);
        summary.Sessions.ShouldBe(1);
        world.Store.Rows.Select(r => r.Row.SessionDate).Distinct().ShouldBe([Monday.AddDays(1)]);
        world.Store.Finished.ShouldHaveSingleItem().Note.ShouldNotBeNull().ShouldContain("1 units failed");
    }

    [Fact]
    public async Task A_rolling_window_that_cannot_be_read_is_skipped_and_the_run_goes_on_and_ends()
    {
        var world = new QualityWorld();
        var bad = Window(QualityWorld.Nifty, 'M', "ok", new DateOnly(2021, 12, 6));
        var good = Window(QualityWorld.BankNifty, 'M', "ok", new DateOnly(2021, 12, 6));
        world.Archive.PendingRolling.AddRange([bad, good]);
        world.Archive.ThrowOnKeys.Add(bad.FetchKey);

        var summary = await world.Runner().RunAsync("daily", Ct);

        summary.Failures.ShouldBe(1);
        world.Store.Rows.Select(r => r.Row.UnderlyingId).Distinct().ShouldBe([QualityWorld.BankNifty]);
    }

    [Fact]
    public async Task Windows_are_taken_in_batches_until_the_limit_so_the_limit_and_not_the_batch_size_is_the_bound()
    {
        var world = new QualityWorld();
        for (var i = 0; i < 450; i++)
        {
            world.Archive.PendingRolling.Add(new RollingWindow(QualityWorld.Nifty, 'M', 1, i - 10 < -10 ? -10 : Math.Min(i % 21 - 10, 10), i % 2 == 0 ? "CE" : "PE", 5, new DateOnly(2021, 12, 6).AddDays(30 * (i / 42)), new DateOnly(2021, 12, 6).AddDays((30 * (i / 42)) + 30), "ok", $"k{i}"));
        }

        world.Settings.MaxRollingWindowsPerRun = 1000;
        var all = await world.Runner().RunAsync("daily", Ct);
        var keys = world.Store.Rows.Select(r => r.Row.FetchKey).Distinct().Count();

        keys.ShouldBe(450);
        all.Stopped.ShouldBeFalse();
    }

    [Fact]
    public async Task The_limit_bounds_the_windows_of_a_run_even_across_batches()
    {
        var world = new QualityWorld();
        for (var i = 0; i < 450; i++)
        {
            world.Archive.PendingRolling.Add(new RollingWindow(QualityWorld.Nifty, 'M', 1, 0, "CE", 5, new DateOnly(2021, 12, 6), new DateOnly(2022, 1, 5), "ok", $"k{i}"));
        }

        world.Settings.MaxRollingWindowsPerRun = 250;
        await world.Runner().RunAsync("daily", Ct);

        world.Store.Rows.Select(r => r.Row.FetchKey).Distinct().Count().ShouldBe(250);
    }

    [Fact]
    public async Task A_day_on_which_no_series_of_the_underlying_has_a_bar_is_a_past_holiday_and_gets_no_verdict()
    {
        var world = new QualityWorld();
        var window = Window(QualityWorld.Nifty, 'M', "ok", new DateOnly(2021, 12, 6));
        world.Archive.PendingRolling.Add(window);
        world.Archive.ActiveDays[(QualityWorld.Nifty, window.From)] = [new DateOnly(2021, 12, 6), new DateOnly(2021, 12, 8)];

        await world.Runner().RunAsync("daily", Ct);

        Of(world, "rolling", "coverage").Select(r => r.SessionDate).Order().ShouldBe([new DateOnly(2021, 12, 6), new DateOnly(2021, 12, 8)]);
    }

    [Fact]
    public async Task With_no_active_day_at_all_the_calendar_decides_so_an_empty_window_is_still_judged()
    {
        var world = new QualityWorld();
        world.Archive.PendingRolling.Add(Window(QualityWorld.Nifty, 'M', "empty", new DateOnly(2021, 12, 6)));

        await world.Runner().RunAsync("daily", Ct);

        Of(world, "rolling", "coverage").Count().ShouldBeGreaterThan(15);
    }

    [Fact]
    public async Task A_failure_that_ends_the_run_is_not_hidden_by_a_failure_to_write_the_closing_row()
    {
        var world = new QualityWorld();
        world.Archive.ThrowOnPendingChain = new InvalidOperationException("original");
        world.Store.ThrowOnFinish = new InvalidOperationException("closing");

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () => await world.Runner().RunAsync("daily", Ct));

        failure.Message.ShouldBe("original");
    }
}
