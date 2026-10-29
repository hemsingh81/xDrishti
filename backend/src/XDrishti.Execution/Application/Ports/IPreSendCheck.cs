using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>The per-intent re-check made just before a send (P12-03 supplies the real one). The default answers a refusal: no answer is never a pass.</summary>
public interface IPreSendCheck
{
    Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken);
}

internal sealed class ClosedPreSendCheck : IPreSendCheck
{
    public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) =>
        Task.FromResult(new GateDecision(false, "no_pre_send_check"));
}
