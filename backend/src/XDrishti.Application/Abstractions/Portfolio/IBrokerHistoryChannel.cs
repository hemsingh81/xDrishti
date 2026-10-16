namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A broker history run that was started (its trades run is recorded as running) and waits to be carried out by the background worker: the account, the first date to read when the owner chose one, and who asked.</summary>
public sealed record BrokerHistoryTicket(Guid RunId, Guid AccountId, DateOnly? From, string Actor);

/// <summary>Hands started broker history runs to the background worker of the API process.</summary>
public interface IBrokerHistoryChannel
{
    void Enqueue(BrokerHistoryTicket ticket);

    IAsyncEnumerable<BrokerHistoryTicket> ReadAllAsync(CancellationToken cancellationToken);
}
