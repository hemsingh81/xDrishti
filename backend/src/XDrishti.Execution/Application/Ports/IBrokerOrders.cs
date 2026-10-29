using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>What one broker request came to: acknowledged, certainly not sent, or unknown (look it up before anything else).</summary>
public enum BrokerOutcome
{
    Acknowledged,
    NotSent,
    Unknown,
}

/// <summary>The protective prices a super-order entry carries (stop-loss and target; the trailing jump is always 0). UNVERIFIED against Dhan (P12-04).</summary>
public sealed record SuperLegs(decimal StopLoss, decimal Target);

/// <summary>
/// One order leg. <see cref="Trigger"/> is the trigger of a stop-loss limit leg (the limit is <see cref="Price"/>, at or below it for a sell); <see cref="Super"/> marks a super-order entry. Both default to null, so every earlier call is unchanged.
/// </summary>
public sealed record BrokerLeg(string ClientOrderId, LegKind Kind, ExecSide Side, string SecurityId, int Qty, decimal Price, decimal? Trigger = null, SuperLegs? Super = null);

/// <summary>The result of one request. <see cref="Reason"/> is a short code (never a body, never a secret): why it was not sent or was refused by the broker.</summary>
public sealed record BrokerAck(BrokerOutcome Outcome, string? BrokerOrderId, string? Reason = null);

/// <summary>A lookup that could not be answered (network, 5xx, unreadable body). It is not the same as "the broker has no such order" (null).</summary>
public sealed class BrokerLookupFailedException(string reason) : Exception(reason);

/// <summary>
/// The one port through which an order could reach a broker. In P12-01 only <see cref="FakeBroker"/> implements it, in memory; the Dhan adapter is P12-02. This is the only interface the boundary test lets name an order action
/// (its methods are named neutrally anyway).
/// </summary>
public interface IBrokerOrders
{
    Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken);

    Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken);

    Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken);

    /// <summary>Finds a leg by our client id; null only when the broker positively has none. Throws <see cref="BrokerLookupFailedException"/> when the answer cannot be read.</summary>
    Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken);
}

/// <summary>
/// Changes a resting stop leg (P12-04). Separate from <see cref="IBrokerOrders"/> so that port stays as it was. Like it, nothing registers an implementation in a host by default. The new trigger and limit are given together; the caller has already
/// proved the move only tightens (see <c>StopRules</c>), and an implementation refuses anything that is not a stop leg.
/// </summary>
public interface IBrokerStops
{
    Task<BrokerAck> ModifyStopAsync(string clientOrderId, decimal trigger, decimal limit, CancellationToken cancellationToken);
}
