using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>A service whose runs the test controls: what a run does, what it was asked, and a signal when a run has started.</summary>
internal sealed class TestService(ServiceDefinition definition) : IScheduledService
{
    private readonly object _gate = new();
    private readonly List<TaskCompletionSource> _startWaiters = [];

    public ServiceDefinition Definition { get; } = definition;

    /// <summary>The first-start settings of the configuration, or null.</summary>
    public ServiceSettings? Seed { get; set; }

    public List<ServiceRunContext> Calls { get; } = [];

    /// <summary>What a run does; by default it succeeds at once.</summary>
    public Func<ServiceRunContext, CancellationToken, ValueTask<ServiceOutcome>> Behavior { get; set; } = (_, _) => ValueTask.FromResult(ServiceOutcome.Succeeded());

    public ServiceSettings? FirstStartSettings() => Seed;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls.Add(context);
            foreach (var waiter in _startWaiters)
            {
                waiter.TrySetResult();
            }

            _startWaiters.Clear();
        }

        return await Behavior(context, cancellationToken);
    }

    /// <summary>Completes when a run starts (or has started since the last call of <see cref="Calls"/> count <paramref name="count"/>).</summary>
    public Task WhenStartedAsync(int count = 1)
    {
        lock (_gate)
        {
            if (Calls.Count >= count)
            {
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startWaiters.Add(waiter);
            return waiter.Task.ContinueWith(_ => WhenStartedAsync(count), TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>A run that does not end until the test says so (or it is cancelled).</summary>
    public TaskCompletionSource<ServiceOutcome> Gate()
    {
        var gate = new TaskCompletionSource<ServiceOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Behavior = async (_, ct) => await gate.Task.WaitAsync(ct);
        return gate;
    }

    /// <summary>Every run fails with the message.</summary>
    public void AlwaysFail(string message) => Behavior = (_, _) => ValueTask.FromResult(ServiceOutcome.Failed(message));
}
