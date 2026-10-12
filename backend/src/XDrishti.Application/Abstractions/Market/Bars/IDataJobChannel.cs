namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>A registered data job waiting to be executed.</summary>
public sealed record DataJobTicket(Guid JobId);

/// <summary>Hands registered data jobs to the background worker of the API process.</summary>
public interface IDataJobChannel
{
    void Enqueue(DataJobTicket ticket);

    IAsyncEnumerable<DataJobTicket> ReadAllAsync(CancellationToken cancellationToken);
}
