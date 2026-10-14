using System.Threading.Channels;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>In-process hand-over of started broker history runs to the API's background worker (the runs are recorded in the database; a run lost with the process is ended as interrupted at the next start).</summary>
internal sealed class BrokerHistoryChannel : IBrokerHistoryChannel
{
    private readonly Channel<BrokerHistoryTicket> _channel = Channel.CreateUnbounded<BrokerHistoryTicket>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(BrokerHistoryTicket ticket) => _channel.Writer.TryWrite(ticket);

    public IAsyncEnumerable<BrokerHistoryTicket> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
