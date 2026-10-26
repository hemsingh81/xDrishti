#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning.Jobs;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning.Jobs;

/// <summary>Spec P8-18 Part B decision D4 and criteria 3 and 9: the dispatcher runs ONE job at a time, never schedules or retries one, and ends the jobs its host left running as interrupted at the start (never resumed).</summary>
public sealed class ExperimentJobDispatcherTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private readonly JobFixtures.FakeJobs _jobs = new();

    private readonly JobFixtures.RecordingProgress _progress = new();

    private readonly List<Guid> _ran = [];

    private int _concurrent;

    private int _highest;

    private ExperimentJobDispatcher Dispatcher()
    {
        var executor = Substitute.For<IExperimentJobExecutor>();
        executor.ExecuteAsync(Arg.Any<ExperimentJob>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var job = call.Arg<ExperimentJob>();
            _highest = Math.Max(_highest, Interlocked.Increment(ref _concurrent));
            lock (_ran)
            {
                _ran.Add(job.Id);
            }

            await Task.Delay(30, Ct);
            await _jobs.FinishAsync(new ExperimentJobFinish(job.Id, ExperimentJobState.Succeeded, "compare", null, 1, null, null, 1, 1, false, Now), Ct);
            Interlocked.Decrement(ref _concurrent);
        });
        var scopes = new Scopes(new Dictionary<Type, object> { [typeof(IExperimentJobStore)] = _jobs, [typeof(IExperimentJobProgress)] = _progress, [typeof(IExperimentJobExecutor)] = executor });
        return new ExperimentJobDispatcher(scopes, new ExperimentJobSettings(), TimeProvider.System, NullLogger<ExperimentJobDispatcher>.Instance);
    }

    private void Queue(int n)
    {
        for (var i = 0; i < n; i++)
        {
            _jobs.Rows.Add(new ExperimentJob(Guid.NewGuid(), new string((char)('a' + i), 64), JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "3", 6, "r", ExperimentJobState.Queued, null, 0, 0, false, null, null, null, null, 2, null, null, false, JobFixtures.Owner, Now.AddSeconds(i), null, null, null, Now));
        }
    }

    [Fact]
    public async Task Queued_jobs_run_one_after_the_other_never_two_at_once_oldest_first()
    {
        Queue(3);

        await Dispatcher().DrainAsync(Ct);

        _ran.ShouldBe([.. _jobs.Rows.Select(r => r.Id)]);
        _highest.ShouldBe(1);
        _jobs.Rows.ShouldAllBe(r => r.State == ExperimentJobState.Succeeded);
    }

    [Fact]
    public async Task With_nothing_queued_nothing_runs_and_a_finished_job_is_never_run_again()
    {
        await Dispatcher().DrainAsync(Ct);
        Queue(1);
        var dispatcher = Dispatcher();
        await dispatcher.DrainAsync(Ct);
        await dispatcher.DrainAsync(Ct);

        _ran.Count.ShouldBe(1);
    }

    [Fact]
    public async Task At_the_start_the_jobs_this_host_left_running_are_interrupted_with_a_final_event_and_not_resumed()
    {
        _jobs.Rows.Add(new ExperimentJob(Guid.NewGuid(), new string('a', 64), JobFixtures.BaselineKey, "exit", "FIXED_RR.rr", "2", "3", 6, "r", ExperimentJobState.Running, "replay", 1, 2, false, null, null, null, null, 2, null, null, false, JobFixtures.Owner, Now, Now, null, ExperimentJobDispatcher.Host, Now));

        var dispatcher = Dispatcher();
        await dispatcher.RecoverAsync(Ct);
        await dispatcher.DrainAsync(Ct);

        _jobs.Rows.Single().State.ShouldBe(ExperimentJobState.Interrupted);
        _progress.Finished.ShouldBe([new ExperimentJobFinished(_jobs.Rows.Single().Id, "interrupted")]);
        _ran.ShouldBeEmpty();
    }

    private sealed class Scopes(Dictionary<Type, object> services) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(services);

        private sealed class Scope(Dictionary<Type, object> services) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);

            public void Dispose()
            {
            }
        }
    }
}
