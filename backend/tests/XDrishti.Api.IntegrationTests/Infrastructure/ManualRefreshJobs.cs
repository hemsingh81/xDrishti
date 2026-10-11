using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the API's in-process job queue: registered refreshes wait here until a test runs them with <see cref="RunPendingAsync"/>, so tests
/// can look at a refresh while it is "running" and never wait on a background thread.
/// </summary>
public sealed class ManualRefreshJobs : IRefreshJobChannel
{
    private readonly ConcurrentQueue<RefreshJob> _pending = new();

    public int PendingCount => _pending.Count;

    public void Enqueue(RefreshJob job) => _pending.Enqueue(job);

    /// <summary>Never yields: the real background worker (which reads this) has nothing to do in tests.</summary>
    public IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken) =>
        Channel.CreateUnbounded<RefreshJob>().Reader.ReadAllAsync(cancellationToken);

    public async Task RunPendingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        while (_pending.TryDequeue(out var job))
        {
            await using var scope = services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteRefreshCommand>>();
            (await handler.HandleAsync(new ExecuteRefreshCommand(job.RunId), cancellationToken)).IsSuccess.ShouldBeTrue();
        }
    }

    public void Clear() => _pending.Clear();
}
