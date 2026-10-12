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
    [InlineData(new[] { "aggregate", "quality" }, true)]
    [InlineData(new[] { "dhan", "aggregate", "quality" }, false)]
    [InlineData(new[] { "quality" }, false)]
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

    [Fact]
    public void A_job_started_without_a_trigger_is_manual_and_does_not_refetch_gaps()
    {
        var job = Start("dhan");

        (job.Trigger, job.RefetchGaps).ShouldBe((DataJobTrigger.Manual, false));
    }

    [Fact]
    public void A_job_carries_the_trigger_that_started_it()
    {
        var job = DataJob.Start(["dhan", "aggregate"], false, "system:data-eod", Now, DataJobTrigger.Daily);

        job.Trigger.ShouldBe(DataJobTrigger.Daily);
        job.Trigger.ToCode().ShouldBe("daily");
        DataJobTrigger.Manual.ToCode().ShouldBe("manual");
    }

    [Fact]
    public void Refetching_gaps_only_counts_when_the_dhan_phase_is_part_of_the_job()
    {
        DataJob.Start(["dhan"], false, "owner", Now, refetchGaps: true).RefetchGaps.ShouldBeTrue();
        DataJob.Start(["aggregate"], false, "owner", Now, refetchGaps: true).RefetchGaps.ShouldBeFalse();
    }

    [Fact]
    public void A_skipped_run_is_ended_at_once_with_the_reason_and_no_phase_ran()
    {
        var job = DataJob.Skipped(["dhan", "aggregate"], DataJobTrigger.Daily, "system:data-eod", "The Data account is not connected.", Now);

        (job.Status, job.IsRunning, job.FinishedAt, job.Error, job.Trigger).ShouldBe((DataJobStatus.Skipped, false, Now, "The Data account is not connected.", DataJobTrigger.Daily));
        new[] { job.Csv.Status, job.Dhan.Status, job.Aggregate.Status }.ShouldAllBe(status => status == PhaseStatus.NotRequested);
        job.Steps.ShouldBe(["dhan", "aggregate"]);
    }

    [Fact]
    public void A_skipped_runs_reason_is_kept_within_the_limit_and_the_run_cannot_be_ended_again()
    {
        var job = DataJob.Skipped(["dhan"], DataJobTrigger.Daily, "x", new string('r', 2000), Now);

        job.Error!.Length.ShouldBe(DataJob.MaxErrorLength);
        Should.Throw<InvalidOperationException>(() => job.Finish(Now));
    }

    [Fact]
    public void The_quality_step_runs_last_and_a_job_without_it_does_not_request_the_phase()
    {
        var job = Start("quality", "aggregate", "dhan");

        job.Steps.ShouldBe(["dhan", "aggregate", "quality"]);
        job.Quality.Status.ShouldBe(PhaseStatus.Pending);
        Start("dhan").Quality.Status.ShouldBe(PhaseStatus.NotRequested);
    }

    [Fact]
    public void Reporting_the_quality_phase_makes_it_the_current_one_and_a_failed_quality_phase_fails_the_job()
    {
        var job = Start("aggregate", "quality");

        job.Report(job.Quality with { Status = PhaseStatus.Running, Total = 10 });
        job.CurrentPhase.ShouldBe("quality");
        job.Report(job.Quality with { Status = PhaseStatus.Failed, Message = "the checks broke" });
        job.Finish(Now.AddMinutes(1));

        (job.Status, job.Error).ShouldBe((DataJobStatus.Failed, "the checks broke"));
    }

    [Fact]
    public void Failing_a_job_fails_a_running_quality_phase_and_skips_a_waiting_one()
    {
        var running = Start("quality");
        running.Report(running.Quality with { Status = PhaseStatus.Running });
        var waiting = Start("aggregate", "quality");

        running.Fail(Now.AddMinutes(1), "stopped");
        waiting.Fail(Now.AddMinutes(1), "stopped");

        running.Quality.Status.ShouldBe(PhaseStatus.Failed);
        waiting.Quality.Status.ShouldBe(PhaseStatus.Skipped);
    }

    [Fact]
    public void A_job_can_be_about_one_instrument_and_the_default_is_every_instrument()
    {
        var id = Guid.NewGuid();

        DataJob.Start(["quality"], false, "owner", Now, instrumentId: id).InstrumentId.ShouldBe(id);
        Start("quality").InstrumentId.ShouldBeNull();
    }

    [Fact]
    public void A_skipped_run_has_no_quality_phase_either()
    {
        DataJob.Skipped(["dhan", "aggregate", "quality"], DataJobTrigger.Daily, "system", "not connected", Now).Quality.Status.ShouldBe(PhaseStatus.NotRequested);
    }

    [Fact]
    public void The_files_a_job_was_asked_to_load_are_kept_without_duplicates_only_with_the_csv_step()
    {
        var withCsv = DataJob.Start(["csv", "aggregate"], false, "owner", Now, csvFiles: ["ABB_1m.csv.gz", "TCS_1m.csv", "ABB_1m.csv.gz"]);
        var withoutCsv = DataJob.Start(["dhan"], false, "owner", Now, csvFiles: ["ABB_1m.csv.gz"]);
        var all = DataJob.Start(["csv"], false, "owner", Now);
        var empty = DataJob.Start(["csv"], false, "owner", Now, csvFiles: []);

        withCsv.CsvFiles.ShouldBe(["ABB_1m.csv.gz", "TCS_1m.csv"]);
        withoutCsv.CsvFiles.ShouldBeNull();
        all.CsvFiles.ShouldBeNull();
        empty.CsvFiles.ShouldBeNull();
        withCsv.UndoCsvFile.ShouldBeNull();
    }

    [Fact]
    public void The_undo_job_is_about_one_instrument_runs_the_csv_step_then_the_aggregates_and_names_the_file()
    {
        var instrument = Guid.CreateVersion7();

        var job = DataJob.StartUndo("ABB_1m.csv.gz", instrument, "owner@example.invalid", Now);

        job.Steps.ShouldBe(["csv", "aggregate"]);
        (job.UndoCsvFile, job.InstrumentId, job.Trigger, job.ForceCsv, job.RebuildAll, job.CsvFiles).ShouldBe(("ABB_1m.csv.gz", instrument, DataJobTrigger.Manual, false, false, null));
        (job.Csv.Status, job.Aggregate.Status, job.Dhan.Status).ShouldBe((PhaseStatus.Pending, PhaseStatus.Pending, PhaseStatus.NotRequested));
    }

    [Fact]
    public void An_undo_job_needs_a_file_name()
    {
        Should.Throw<ArgumentException>(() => DataJob.StartUndo(" ", Guid.CreateVersion7(), "owner", Now));
    }
}
