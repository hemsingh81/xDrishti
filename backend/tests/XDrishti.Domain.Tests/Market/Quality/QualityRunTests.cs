using XDrishti.Domain.Market.Quality;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class QualityRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 20, 0, TimeSpan.Zero);

    private static QualityRun Started() => QualityRun.Start(QualityTrigger.Daily, 5, 214, Guid.NewGuid(), null, Now);

    [Fact]
    public void A_started_run_is_running_with_nothing_counted()
    {
        var run = Started();

        (run.Status, run.Trigger, run.Sessions, run.Instruments, run.Passed, run.Warned, run.Failed, run.FinishedAt, run.Score).ShouldBe((QualityRunStatus.Running, QualityTrigger.Daily, 5, 214, 0, 0, 0, null, null));
        run.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void Each_result_counts_in_its_own_tally_and_a_run_that_has_ended_counts_nothing_more()
    {
        var run = Started();

        run.Record(CheckStatus.Pass);
        run.Record(CheckStatus.Pass);
        run.Record(CheckStatus.Warning);
        run.Record(CheckStatus.Fail);
        run.Finish(Now.AddSeconds(5), 91.26);
        run.Record(CheckStatus.Fail);

        (run.Passed, run.Warned, run.Failed).ShouldBe((2, 1, 1));
        (run.Status, run.FinishedAt, run.Score).ShouldBe((QualityRunStatus.Succeeded, Now.AddSeconds(5), 91.3));
    }

    [Fact]
    public void A_result_must_be_pass_warning_or_fail()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Started().Record(CheckStatus.NotAvailable));
    }

    [Fact]
    public void A_failed_run_keeps_the_reason_cut_to_the_limit_and_cannot_end_twice()
    {
        var run = Started();

        run.Fail(Now.AddSeconds(1), new string('x', 2000));

        (run.Status, run.Error!.Length).ShouldBe((QualityRunStatus.Failed, QualityRun.MaxErrorLength));
        Should.Throw<InvalidOperationException>(() => run.Finish(Now.AddSeconds(2), 50));
    }

    [Fact]
    public void A_run_over_one_instrument_remembers_it_and_a_run_without_instruments_scores_nothing()
    {
        var id = Guid.NewGuid();
        var run = QualityRun.Start(QualityTrigger.Manual, 5, 1, null, id, Now);

        run.Finish(Now, null);

        (run.InstrumentId, run.JobId, run.Score).ShouldBe((id, null, null));
    }

    [Theory]
    [InlineData(QualityTrigger.Manual, "manual")]
    [InlineData(QualityTrigger.Daily, "daily")]
    [InlineData(QualityTrigger.AfterJob, "after_job")]
    public void The_trigger_codes_are_stable_and_round_trip(QualityTrigger trigger, string code)
    {
        trigger.ToCode().ShouldBe(code);
        QualityTriggerCodes.FromCode(code).ShouldBe(trigger);
    }
}
