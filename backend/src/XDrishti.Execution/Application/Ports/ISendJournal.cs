namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// The write-ahead record of a send. <see cref="BeginAsync"/> must be committed before the request leaves; an exception from it means nothing is sent. The persistent implementation (leg row, event, audit row) is P12-02b; until then only
/// the in-memory one used by tests exists and nothing registers it.
/// </summary>
public interface ISendJournal
{
    Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken);

    /// <summary>The leg was dropped after the write-ahead and before the request (a halt appeared meanwhile).</summary>
    Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken);

    Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken);
}
