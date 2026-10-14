namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Deployment setting of the scheduled trade and ledger read (<c>Portfolio:BrokerHistory:Enabled</c>, a first-start seed of the service, ADR 0018 §3). "Sync now" works either way.</summary>
public interface IBrokerHistorySettings
{
    /// <summary>Whether the service starts enabled (default true; tests start it paused).</summary>
    bool Enabled { get; }
}
