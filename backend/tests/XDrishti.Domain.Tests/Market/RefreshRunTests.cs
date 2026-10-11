using XDrishti.Domain.Market;

namespace XDrishti.Domain.Tests.Market;

public sealed class RefreshRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 3, 15, 0, TimeSpan.Zero);

    private static RefreshOutcome Outcome(IReadOnlyList<string>? warnings = null) =>
        new(10, 2, 3, 1, 40, 150, 0, 30, 20, 8, 56, 207_560, true, new Dictionary<string, int> { ["share"] = 40 }, warnings ?? []);

    private static RefreshRun Started() => RefreshRun.Start(RefreshTrigger.Manual, false, "owner@example.invalid", Now);

    [Fact]
    public void A_new_run_is_running_in_the_download_phase()
    {
        var run = Started();

        (run.Status, run.Phase, run.IsRunning, run.StartedAt, run.FinishedAt).ShouldBe((RefreshStatus.Running, RefreshPhase.Downloading, true, Now, null));
        run.Id.ShouldNotBe(Guid.Empty);
        run.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Progress_moves_the_phase_and_row_count_while_running()
    {
        var run = Started();

        run.ReportProgress(RefreshPhase.Parsing, 40_000);

        (run.Phase, run.RowsRead).ShouldBe((RefreshPhase.Parsing, 40_000));
    }

    [Fact]
    public void Success_records_the_counts_and_the_stamps_and_clears_the_phase()
    {
        var run = Started();
        run.ReportProgress(RefreshPhase.Saving, 5);

        run.Succeed(Now.AddSeconds(30), new SourceStamp("\"abc\"", "Sat"), new SourceStamp(null, "Fri"), Outcome(["careful"]));

        (run.Status, run.Phase, run.FinishedAt).ShouldBe((RefreshStatus.Succeeded, null, Now.AddSeconds(30)));
        (run.Added, run.Updated, run.Retired, run.Revived, run.Unchanged, run.SkippedByScope, run.TotalActive, run.RowsRead).ShouldBe((10, 2, 3, 1, 40, 150, 56, 207_560));
        (run.DhanETag, run.DhanLastModified, run.SectorETag, run.SectorLastModified).ShouldBe(("\"abc\"", "Sat", null, "Fri"));
        run.KeptByType["share"].ShouldBe(40);
        run.Warnings.ShouldBe(["careful"]);
    }

    [Fact]
    public void A_run_records_the_rules_it_worked_under_and_whether_sectors_were_applied()
    {
        var run = Started();
        run.RulesVersion.ShouldBe(RefreshRules.Version);
        run.SectorsApplied.ShouldBeFalse();

        run.Succeed(Now, SourceStamp.None, SourceStamp.None, Outcome() with { SectorsApplied = true });

        run.SectorsApplied.ShouldBeTrue();
        var without = Started();
        without.Succeed(Now, SourceStamp.None, SourceStamp.None, Outcome() with { SectorsApplied = false });
        without.SectorsApplied.ShouldBeFalse();
    }

    [Fact]
    public void An_unchanged_run_carries_the_stamps_and_counts_nothing()
    {
        var run = Started();

        run.MarkUnchanged(Now.AddSeconds(2), new SourceStamp("\"abc\"", null), SourceStamp.None, 4381);

        (run.Status, run.TotalActive, run.Added, run.DhanETag).ShouldBe((RefreshStatus.Unchanged, 4381, 0, "\"abc\""));
    }

    [Fact]
    public void A_failed_run_keeps_the_error_and_no_counts()
    {
        var run = Started();

        run.Fail(Now.AddSeconds(1), "Dhan answered HTTP 503", ["a warning"]);

        (run.Status, run.Error, run.Added, run.Phase).ShouldBe((RefreshStatus.Failed, "Dhan answered HTTP 503", 0, null));
        run.Warnings.ShouldBe(["a warning"]);
    }

    [Fact]
    public void An_ended_run_cannot_end_again_nor_report_progress()
    {
        var run = Started();
        run.Fail(Now, "x");

        Should.Throw<InvalidOperationException>(() => run.Succeed(Now, SourceStamp.None, SourceStamp.None, Outcome()));
        Should.Throw<InvalidOperationException>(() => run.Fail(Now, "y"));
        run.ReportProgress(RefreshPhase.Parsing, 99);
        run.RowsRead.ShouldBe(0);
        run.Phase.ShouldBeNull();
    }

    [Fact]
    public void Warnings_and_the_error_are_capped()
    {
        var run = Started();
        var warnings = Enumerable.Range(0, 70).Select(i => new string('w', 400) + i).ToList();

        run.Succeed(Now, SourceStamp.None, SourceStamp.None, Outcome(warnings));

        run.Warnings.Count.ShouldBe(RefreshRun.MaxWarnings + 1);
        run.Warnings.Take(RefreshRun.MaxWarnings).ShouldAllBe(w => w.Length == RefreshRun.MaxWarningLength);
        run.Warnings[^1].ShouldContain("20 more");

        var failed = Started();
        failed.Fail(Now, new string('e', 5000));
        failed.Error!.Length.ShouldBe(RefreshRun.MaxErrorLength);
    }

    [Fact]
    public void The_requester_is_kept_but_limited_in_length()
    {
        RefreshRun.Start(RefreshTrigger.Daily, false, new string('x', 400), Now).RequestedBy.Length.ShouldBe(RefreshRun.MaxRequestedByLength);
    }
}
