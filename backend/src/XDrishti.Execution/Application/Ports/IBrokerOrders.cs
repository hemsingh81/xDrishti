using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>What one broker request came to: acknowledged, certainly not sent, or unknown (look it up before anything else).</summary>
public enum BrokerOutcome
{
    Acknowledged,
    NotSent,
    Unknown,
}

public sealed record BrokerLeg(string ClientOrderId, LegKind Kind, ExecSide Side, string SecurityId, int Qty, decimal Price);

public sealed record BrokerAck(BrokerOutcome Outcome, string? BrokerOrderId);

/// <summary>
/// The one port through which an order could reach a broker. In P12-01 only <see cref="FakeBroker"/> implements it, in memory; the Dhan adapter is P12-02. This is the only interface the boundary test lets name an order action
/// (its methods are named neutrally anyway).
/// </summary>
public interface IBrokerOrders
{
    Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken);

    Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken);

    Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken);

    /// <summary>Finds a leg by our client id; null when the broker has none.</summary>
    Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken);
}
