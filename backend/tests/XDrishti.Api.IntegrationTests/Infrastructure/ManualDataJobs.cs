using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the API's in-process queue of data jobs: registered jobs wait here until a test runs them with <see cref="RunPendingAsync"/>, so tests can look at
/// a job while it is "running" and never wait on a background thread.
/// </summary>
public sealed class ManualDataJobs : IDataJobChannel
{
    private readonly ConcurrentQueue<DataJobTicket> _pending = new();

    public int PendingCount => _pending.Count;

    public void Enqueue(DataJobTicket ticket) => _pending.Enqueue(ticket);

    /// <summary>Never yields: the real background worker (which reads this) has nothing to do in tests.</summary>
    public IAsyncEnumerable<DataJobTicket> ReadAllAsync(CancellationToken cancellationToken) =>
        Channel.CreateUnbounded<DataJobTicket>().Reader.ReadAllAsync(cancellationToken);

    public async Task RunPendingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        while (_pending.TryDequeue(out var ticket))
        {
            await using var scope = services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteDataJobCommand>>();
            (await handler.HandleAsync(new ExecuteDataJobCommand(ticket.JobId), cancellationToken)).IsSuccess.ShouldBeTrue();
        }
    }

    public void Clear() => _pending.Clear();
}
