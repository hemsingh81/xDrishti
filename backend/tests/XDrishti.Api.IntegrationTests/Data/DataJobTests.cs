using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>Starting data jobs: one at a time, the steps, the history, and what happens to a job that was running when the application stopped.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataJobTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task A_new_job_is_registered_as_running_with_every_phase_pending_and_runs_in_the_background()
    {
        await SeedAsync("ABB");

        var started = await StartAsync();

        started.Status.ShouldBe("running");
        started.Steps.ShouldBe(["csv", "dhan", "aggregate"]);
        (started.ForceCsv, started.RebuildAll, started.CurrentPhase, started.FinishedAt).ShouldBe((false, false, null, null));
        (started.Csv.Status, started.Dhan.Status, started.Aggregate.Status).ShouldBe(("pending", "pending", "pending"));
        started.RequestedBy.ShouldBe(ApiFactory.OwnerEmail);
        Factory.DataJobs.PendingCount.ShouldBe(1);
        (await GetJobAsync(started.Id)).Status.ShouldBe("running");
        (await OverviewAsync()).RunningJob!.Id.ShouldBe(started.Id);
    }

    [Fact]
    public async Task While_a_job_runs_another_is_refused_and_after_it_a_new_one_can_start()
    {
        await SeedAsync("ABB");
        var first = await StartAsync();

        using var second = await PostRefreshAsync();
        using var third = await PostRefreshAsync(["aggregate"]);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync(Ct)).Code.ShouldBe("data.job_running");
        third.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        (await GetJobAsync(first.Id)).Status.ShouldBe("succeeded");
        using var after = await PostRefreshAsync();
        after.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Two_starts_at_the_same_moment_register_exactly_one_job()
    {
        await SeedAsync("ABB");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostRefreshAsync()));

        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
            (await ScalarAsync<long>("SELECT count(*) FROM market.data_jobs WHERE status = 'running'")).ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_job_that_was_running_when_the_application_stopped_is_marked_failed_at_start_up_and_no_longer_blocks()
    {
        await SeedAsync("ABB");
        var stuck = await StartAsync();
        Factory.DataJobs.Clear();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var recover = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedDataJobsCommand>>();
            (await recover.HandleAsync(new RecoverInterruptedDataJobsCommand(Now.AddMinutes(5)), Ct)).IsSuccess.ShouldBeTrue();
        }

        var job = await GetJobAsync(stuck.Id);
        job.Status.ShouldBe("failed");
        job.Error.ShouldBe("The update did not finish (the application stopped or crashed while it ran).");
        job.FinishedAt.ShouldNotBeNull();
        (job.Csv.Status, job.Dhan.Status, job.Aggregate.Status).ShouldBe(("skipped", "skipped", "skipped"));
        job.Csv.Message.ShouldBe("Not run: the update stopped before this step.");
        (await OverviewAsync()).RunningJob.ShouldBeNull();
        using var again = await PostRefreshAsync();
        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_job_that_has_run_for_half_a_day_counts_as_interrupted_and_a_new_one_may_start()
    {
        await SeedAsync("ABB");
        var stuck = await StartAsync();
        Factory.DataJobs.Clear();
        Factory.Clock.Advance(TimeSpan.FromHours(13));
        await ResignInAsync();

        using var again = await PostRefreshAsync();

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await GetJobAsync(stuck.Id)).Status.ShouldBe("failed");
    }

    [Fact]
    public async Task Steps_that_are_missing_unknown_or_empty_are_refused_with_the_field()
    {
        await SeedAsync("ABB");

        using var empty = await PostRefreshAsync([]);
        using var unknown = await PostRefreshAsync(["csv", "everything"]);

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await empty.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["steps"]);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["steps"]);
        Factory.DataJobs.PendingCount.ShouldBe(0);
    }

    [Fact]
    public async Task Steps_always_run_in_pipeline_order_and_duplicates_collapse_and_force_applies_only_to_csv()
    {
        await SeedAsync("ABB");

        var onlyAggregate = await StartAsync(["aggregate", "aggregate"], forceCsv: true);

        onlyAggregate.Steps.ShouldBe(["aggregate"]);
        onlyAggregate.ForceCsv.ShouldBeFalse();
        onlyAggregate.RebuildAll.ShouldBeTrue();
        (onlyAggregate.Csv.Status, onlyAggregate.Dhan.Status, onlyAggregate.Aggregate.Status).ShouldBe(("notRequested", "notRequested", "pending"));
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);

        var dhanAndCsv = await StartAsync(["dhan", "csv"]);

        dhanAndCsv.Steps.ShouldBe(["csv", "dhan"]);
        dhanAndCsv.RebuildAll.ShouldBeFalse();
    }

    [Fact]
    public async Task The_history_lists_the_newest_job_first_and_one_job_can_be_read_by_id()
    {
        await SeedAsync("ABB");
        var first = await RunAsync(["aggregate"]);
        var second = await RunAsync(["csv"]);
        var third = await RunAsync(["dhan"]);

        using var response = await Client.GetAsync("/api/data/jobs", Ct);
        var jobs = await response.ReadAsync<List<DataJobView>>(Ct);
        using var limited = await Client.GetAsync("/api/data/jobs?take=2", Ct);
        var two = await limited.ReadAsync<List<DataJobView>>(Ct);
        using var clamped = await Client.GetAsync("/api/data/jobs?take=0", Ct);
        var one = await clamped.ReadAsync<List<DataJobView>>(Ct);

        jobs.Select(j => j.Id).ShouldBe([third.Id, second.Id, first.Id]);
        two.Select(j => j.Id).ShouldBe([third.Id, second.Id]);
        one.Select(j => j.Id).ShouldBe([third.Id]);
        (await GetJobAsync(second.Id)).Steps.ShouldBe(["csv"]);
    }

    [Fact]
    public async Task An_unknown_job_is_404()
    {
        using var response = await Client.GetAsync($"/api/data/jobs/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.job_not_found");
    }

    [Fact]
    public async Task A_job_without_a_body_runs_all_three_phases_and_the_dhan_phase_is_skipped_with_its_reason()
    {
        await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 30));

        using var response = await Client.PostAsync("/api/data/refresh", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var job = await GetJobAsync(started.Id);

        job.Status.ShouldBe("succeeded", job.Error);
        job.Csv.Status.ShouldBe("succeeded");
        job.Dhan.Status.ShouldBe("skipped");
        job.Dhan.Code.ShouldBe("data.dhan_not_connected");
        job.Dhan.Message.ShouldBe("No account has the primary Data role. Add or choose one under Accounts.");
        job.Aggregate.Status.ShouldBe("succeeded");
        (job.Aggregate.Done, job.Aggregate.Total).ShouldBe((1, 1));
        job.StartedAt.ShouldBe(Now, TimeSpan.FromSeconds(5));
        job.FinishedAt.ShouldNotBeNull();
    }
}
