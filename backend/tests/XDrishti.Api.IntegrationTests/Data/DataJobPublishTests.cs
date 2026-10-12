using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>A worker that is still busy must not overwrite the verdict of a job that was failed meanwhile (recovery at start-up, or the 12-hour rule).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataJobPublishTests(ApiFactory factory) : DataTestBase(factory)
{
    private async Task<DataJob> SnapshotAsync(Guid id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IDataJobStore>().GetAsync(id, Ct))!;
    }

    private async Task<bool> PublishAsync(DataJob job)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDataJobStore>().PublishAsync(job, Ct);
    }

    [Fact]
    public async Task A_running_job_is_written_and_tells_so()
    {
        await SeedAsync("ABB");
        var started = await StartAsync();
        var job = await SnapshotAsync(started.Id);
        job.Report(job.Csv with { Status = PhaseStatus.Running, Total = 7, Done = 3 });

        (await PublishAsync(job)).ShouldBeTrue();

        var seen = await GetJobAsync(started.Id);
        (seen.Status, seen.CurrentPhase, seen.Csv.Done, seen.Csv.Total).ShouldBe(("running", "csv", 3, 7));
    }

    [Fact]
    public async Task A_worker_that_reports_after_recovery_failed_the_job_changes_nothing_and_is_told_to_stop()
    {
        await SeedAsync("ABB");
        var started = await StartAsync();
        var worker = await SnapshotAsync(started.Id); // what the busy worker holds
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var recover = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedDataJobsCommand>>();
            (await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now.AddMinutes(5)), Ct)).IsSuccess.ShouldBeTrue();
        }

        worker.Report(worker.Csv with { Status = PhaseStatus.Running, Done = 5 });
        var inFlight = await PublishAsync(worker);
        worker.Finish(Now.AddMinutes(9));
        var finished = await PublishAsync(worker);

        (inFlight, finished).ShouldBe((false, false));
        var seen = await GetJobAsync(started.Id);
        seen.Status.ShouldBe("failed");
        seen.Error.ShouldBe("The update did not finish (the application stopped or crashed while it ran).");
        seen.Csv.Done.ShouldBe(0);
        seen.FinishedAt.ShouldNotBeNull();
        seen.FinishedAt.Value.ShouldNotBe(Now.AddMinutes(9));
    }

    [Fact]
    public async Task A_worker_whose_job_was_failed_by_the_twelve_hour_rule_cannot_overwrite_it_and_the_new_job_is_not_touched()
    {
        await SeedAsync("ABB");
        var stale = await StartAsync();
        var worker = await SnapshotAsync(stale.Id);
        Factory.DataJobs.Clear();
        Factory.Clock.Advance(TimeSpan.FromHours(13));
        await ResignInAsync();
        var replacement = await StartAsync();
        worker.Report(worker.Csv with { Status = PhaseStatus.Running, Done = 2 });

        (await PublishAsync(worker)).ShouldBeFalse();

        var old = await GetJobAsync(stale.Id);
        (old.Status, old.Csv.Done).ShouldBe(("failed", 0));
        (await GetJobAsync(replacement.Id)).Status.ShouldBe("running");
        var current = await SnapshotAsync(replacement.Id);
        current.Report(current.Csv with { Status = PhaseStatus.Running, Done = 1 });
        (await PublishAsync(current)).ShouldBeTrue();
    }

    [Fact]
    public async Task Executing_a_job_that_was_ended_elsewhere_does_nothing()
    {
        await SeedAsync("ABB");
        var started = await StartAsync();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var recover = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedDataJobsCommand>>();
            await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now.AddMinutes(5)), Ct);
        }

        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);

        (await GetJobAsync(started.Id)).Status.ShouldBe("failed");
    }
}
