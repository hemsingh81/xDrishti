using System.Threading.Channels;
using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Infrastructure.Market;

/// <summary>In-process hand-over of registered refreshes to the API's background worker (registered runs are recorded in the database).</summary>
internal sealed class RefreshJobChannel : IRefreshJobChannel
{
    private readonly Channel<RefreshJob> _channel = Channel.CreateUnbounded<RefreshJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(RefreshJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
