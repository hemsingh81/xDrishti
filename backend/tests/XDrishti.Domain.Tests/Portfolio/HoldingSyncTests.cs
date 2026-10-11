using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

public sealed class HoldingSyncTests
{
    [Fact]
    public void A_started_run_is_running_until_it_succeeds_with_its_counts()
    {
        var run = HoldingSync.Start(HoldingData.Account, SyncTrigger.Manual, "owner@example.invalid", HoldingData.T0);

        run.IsRunning.ShouldBeTrue();
        run.Succeed(HoldingData.T0.AddSeconds(3), new SyncCounts(1, 2, 3, 4, 5));

        (run.Status, run.FinishedAt, run.Counts).ShouldBe((SyncStatus.Succeeded, HoldingData.T0.AddSeconds(3), new SyncCounts(1, 2, 3, 4, 5)));
        Should.Throw<InvalidOperationException>(() => run.Fail(HoldingData.T0, "again"));
    }

    [Fact]
    public void A_failed_run_keeps_a_short_error_text()
    {
        var run = HoldingSync.Start(HoldingData.Account, SyncTrigger.Auto, "system:portfolio-refresh", HoldingData.T0);

        run.Fail(HoldingData.T0.AddSeconds(1), new string('x', 1000));

        (run.Status, run.Error!.Length).ShouldBe((SyncStatus.Failed, HoldingSync.MaxErrorLength));
    }

    [Fact]
    public void A_skipped_run_is_finished_at_once_with_its_reason()
    {
        var run = HoldingSync.Skipped(HoldingData.Account, SyncTrigger.Auto, "system:portfolio-refresh", "The account is not connected to Dhan.", HoldingData.T0);

        (run.Status, run.FinishedAt, run.Error, run.IsRunning).ShouldBe((SyncStatus.Skipped, (DateTimeOffset?)HoldingData.T0, "The account is not connected to Dhan.", false));
    }

    [Fact]
    public void Counts_add_up_per_change_and_know_when_anything_but_prices_moved()
    {
        var counts = SyncCounts.None.Count(HoldingChange.Unchanged).Count(HoldingChange.Unchanged);
        counts.HasChanges.ShouldBeFalse();

        counts = counts.Count(HoldingChange.Added).Count(HoldingChange.Increased).Count(HoldingChange.Decreased).Count(HoldingChange.Closed);

        counts.ShouldBe(new SyncCounts(1, 1, 1, 1, 2));
        (counts.HasChanges, counts.Total).ShouldBe((true, 6));
    }
}
