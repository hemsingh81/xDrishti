using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Tests.Lab;

/// <summary>P5-02 criteria 7 and 9: at most <c>max_parallel_runs</c> runs at once, the rest stay queued until a slot frees; a restart ends the runs the host had running as interrupted and says so.</summary>
public sealed class LabRunDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LabRun Queued() => new(
        Guid.NewGuid(), new string('k', 64), "S", 1, new string('a', 64),
        new LabUniverse("symbols", null, "l", 1, [new LabInstrument(Guid.NewGuid(), "A")], false, null),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), "quick", new string('c', 64), "sim-5", LabRunState.Running, "validate", 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    /// <summary>One scope that is its own provider: enough for the dispatcher, which only asks for three services.</summary>
    private sealed class Scopes(Dictionary<Type, object> services) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);

        public void Dispose()
        {
        }
    }

    private sealed class GatedExecutor : ILabRunExecutor
    {
        private readonly object _lock = new();

        private readonly Dictionary<Guid, TaskCompletionSource> _gates = [];

        public SemaphoreSlim Started { get; } = new(0);

        public int Running { get; private set; }

        public int MostAtOnce { get; private set; }

        public List<Guid> Order { get; } = [];

        public async Task ExecuteAsync(LabRun run, CancellationToken hostStop)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _gates[run.Id] = gate;
                Order.Add(run.Id);
                Running++;
                MostAtOnce = Math.Max(MostAtOnce, Running);
            }

            Started.Release();
            await gate.Task.WaitAsync(hostStop);
            lock (_lock)
            {
                Running--;
            }
        }

        public void Release(Guid id)
        {
            lock (_lock)
            {
                _gates[id].SetResult();
            }
        }
    }

    private static (LabRunDispatcher Dispatcher, ILabRunStore Store, GatedExecutor Executor, ILabProgress Progress) Build(int parallel, params LabRun[] queued)
    {
        var store = Substitute.For<ILabRunStore>();
        var line = new Queue<LabRun>(queued);
        store.ClaimNextAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => line.Count > 0 ? line.Dequeue() : null);
        var executor = new GatedExecutor();
        var progress = Substitute.For<ILabProgress>();
        var scopes = new Scopes(new Dictionary<Type, object> { [typeof(ILabRunStore)] = store, [typeof(ILabRunExecutor)] = executor, [typeof(ILabProgress)] = progress });
        var settings = LabSettings.Default with { MaxParallelRuns = parallel };
        return (new LabRunDispatcher(scopes, settings, TimeProvider.System, NullLogger<LabRunDispatcher>.Instance), store, executor, progress);
    }

    [Fact]
    public async Task With_two_slots_and_three_queued_runs_two_run_and_the_third_waits_until_a_slot_frees()
    {
        var runs = new[] { Queued(), Queued(), Queued() };
        var (dispatcher, store, executor, _) = Build(2, runs);

        var draining = dispatcher.DrainAsync(Ct);
        await executor.Started.WaitAsync(Ct);
        await executor.Started.WaitAsync(Ct);

        executor.Order.ShouldBe([runs[0].Id, runs[1].Id], "the oldest two, in order");
        executor.Running.ShouldBe(2);
        draining.IsCompleted.ShouldBeFalse();
        executor.Started.CurrentCount.ShouldBe(0, "the third has not started");
        store.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILabRunStore.ClaimNextAsync)).ShouldBe(2, "a full house claims nothing more; the third stays queued in the table");

        executor.Release(runs[0].Id);
        await executor.Started.WaitAsync(Ct);

        executor.Order.ShouldBe([runs[0].Id, runs[1].Id, runs[2].Id]);
        executor.Release(runs[1].Id);
        executor.Release(runs[2].Id);
        await draining.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        executor.MostAtOnce.ShouldBe(2);
    }

    [Fact]
    public async Task With_one_slot_the_runs_go_one_after_the_other()
    {
        var runs = new[] { Queued(), Queued() };
        var (dispatcher, _, executor, _) = Build(1, runs);

        var draining = dispatcher.DrainAsync(Ct);
        await executor.Started.WaitAsync(Ct);
        executor.Release(runs[0].Id);
        await executor.Started.WaitAsync(Ct);
        executor.Release(runs[1].Id);
        await draining.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        executor.MostAtOnce.ShouldBe(1);
    }

    [Fact]
    public async Task Recover_ends_the_runs_the_host_had_running_as_interrupted_and_sends_their_last_event()
    {
        var (dispatcher, store, _, progress) = Build(2);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        store.InterruptRunningAsync(LabRunDispatcher.Host, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);

        await dispatcher.RecoverAsync(Ct);

        foreach (var id in ids)
        {
            await progress.Received(1).FinishedAsync(new LabRunFinished(id, "interrupted"), Arg.Any<CancellationToken>());
        }
    }
}
