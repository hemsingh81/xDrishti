using System.Threading.Channels;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>In-process hand-over of registered data jobs to the API's background worker (registered jobs are recorded in the database).</summary>
internal sealed class DataJobChannel : IDataJobChannel
{
    private readonly Channel<DataJobTicket> _channel = Channel.CreateUnbounded<DataJobTicket>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(DataJobTicket ticket) => _channel.Writer.TryWrite(ticket);

    public IAsyncEnumerable<DataJobTicket> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
