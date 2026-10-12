using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class DataJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static DataJob Start(params string[] steps) => DataJob.Start(steps, false, "owner@example.invalid", Now);

    [Fact]
    public void A_job_runs_the_steps_in_pipeline_order_each_once_and_the_others_are_not_requested()
    {
        var job = Start("aggregate", "csv", "csv");

        job.Steps.ShouldBe(["csv", "aggregate"]);
        job.Status.ShouldBe(DataJobStatus.Running);
        (job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe((PhaseStatus.Pending, PhaseStatus.NotRequested, PhaseStatus.Pending));
        job.Requested("csv").ShouldBeTrue();
        job.Requested("dhan").ShouldBeFalse();
        (job.StartedAt, job.FinishedAt, job.CurrentPhase, job.Error).ShouldBe((Now, null, null, null));
        job.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_steps_are_ignored_and_a_job_without_a_known_step_is_refused()
    {
        Start("dhan", "bogus").Steps.ShouldBe(["dhan"]);
        Should.Throw<ArgumentException>(() => Start("bogus"));
        Should.Throw<ArgumentException>(() => Start());
    }

    [Theory]
    [InlineData(new[] { "aggregate" }, true)]
    [InlineData(new[] { "csv", "aggregate" }, false)]
    [InlineData(new[] { "dhan", "aggregate" }, false)]
    [InlineData(new[] { "csv", "dhan", "aggregate" }, false)]
    public void The_aggregates_are_rebuilt_for_everything_only_when_the_job_writes_no_bars(string[] steps, bool rebuildAll)
    {
        var job = DataJob.Start(steps, false, "owner", Now);

        job.RebuildAll.ShouldBe(rebuildAll);
        job.Aggregate.Full.ShouldBe(rebuildAll);
    }

    [Fact]
    public void Forcing_csv_only_counts_when_the_csv_phase_is_part_of_the_job()
    {
        DataJob.Start(["csv"], true, "owner", Now).ForceCsv.ShouldBeTrue();
        DataJob.Start(["dhan"], true, "owner", Now).ForceCsv.ShouldBeFalse();
    }

    [Fact]
    public void The_requester_is_kept_within_its_limit()
    {
        DataJob.Start(["csv"], false, new string('x', 400), Now).RequestedBy.Length.ShouldBe(DataJob.MaxRequestedByLength);
        DataJob.Start(["csv"], false, "  ", Now).RequestedBy.ShouldBe("unknown");
    }

    [Fact]
    public void Reporting_a_running_phase_makes_it_the_current_one_and_a_finished_phase_does_not_change_it()
    {
        var job = Start("csv", "dhan");

        job.Report(job.Csv with { Status = PhaseStatus.Running, Done = 3, Total = 10 });
        job.CurrentPhase.ShouldBe("csv");
        job.Csv.Done.ShouldBe(3);
        job.Report(job.Csv with { Status = PhaseStatus.Succeeded });
        job.CurrentPhase.ShouldBe("csv");
        job.Report(job.Dhan with { Status = PhaseStatus.Running });

        job.CurrentPhase.ShouldBe("dhan");
    }

    [Fact]
    public void A_job_succeeds_when_no_phase_failed_and_a_skipped_phase_is_not_a_failure()
    {
        var job = Start("csv", "dhan", "aggregate");
        job.Report(job.Csv with { Status = PhaseStatus.Succeeded });
        job.Report(job.Dhan with { Status = PhaseStatus.Skipped, Message = "no account" });
        job.Report(job.Aggregate with { Status = PhaseStatus.Succeeded });

        job.Finish(Now.AddMinutes(3));

        (job.Status, job.Error, job.FinishedAt, job.CurrentPhase).ShouldBe((DataJobStatus.Succeeded, null, Now.AddMinutes(3), null));
    }

    [Fact]
    public void A_job_fails_with_the_message_of_the_first_failed_phase()
    {
        var job = Start("csv", "dhan", "aggregate");
        job.Report(job.Csv with { Status = PhaseStatus.Failed, Message = "2 files failed" });
        job.Report(job.Dhan with { Status = PhaseStatus.Failed, Message = "token refused" });
        job.Report(job.Aggregate with { Status = PhaseStatus.Succeeded });

        job.Finish(Now);

        (job.Status, job.Error).ShouldBe((DataJobStatus.Failed, "2 files failed"));
    }

    [Fact]
    public void Failing_a_job_fails_the_running_phase_skips_the_waiting_ones_and_keeps_the_finished_ones()
    {
        var job = Start("csv", "dhan", "aggregate");
        job.Report(job.Csv with { Status = PhaseStatus.Succeeded, Rows = 100 });
        job.Report(job.Dhan with { Status = PhaseStatus.Running });

        job.Fail(Now.AddMinutes(1), "crashed");

        (job.Status, job.Error, job.CurrentPhase).ShouldBe((DataJobStatus.Failed, "crashed", null));
        (job.Csv.Status, job.Csv.Rows).ShouldBe((PhaseStatus.Succeeded, 100));
        (job.Dhan.Status, job.Dhan.Message, job.Dhan.FinishedAt).ShouldBe((PhaseStatus.Failed, "crashed", Now.AddMinutes(1)));
        (job.Aggregate.Status, job.Aggregate.Message).ShouldBe((PhaseStatus.Skipped, "Not run: the update stopped before this step."));
    }

    [Fact]
    public void An_ended_job_accepts_no_more_reports_and_cannot_end_twice()
    {
        var job = Start("csv");
        job.Finish(Now);

        job.Report(job.Csv with { Status = PhaseStatus.Running, Done = 9 });
        job.AddWarning("late");

        job.Csv.Done.ShouldBe(0);
        job.Warnings.ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => job.Finish(Now));
        Should.Throw<InvalidOperationException>(() => job.Fail(Now, "again"));
    }

    [Fact]
    public void Warnings_and_the_error_are_cut_to_their_limits_and_the_list_is_capped()
    {
        var job = Start("csv");
        for (var i = 0; i < DataJob.MaxWarnings + 10; i++)
        {
            job.AddWarning(new string('w', 500));
        }

        job.Warnings.Count.ShouldBe(DataJob.MaxWarnings + 1);
        job.Warnings[0].Length.ShouldBe(DataJob.MaxWarningLength);
        job.Warnings[^1].ShouldBe("…and more warnings.");
        job.Fail(Now, new string('e', 3000));
        job.Error!.Length.ShouldBe(DataJob.MaxErrorLength);
    }
}
