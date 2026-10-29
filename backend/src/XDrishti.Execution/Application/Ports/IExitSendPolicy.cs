using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// The rule for legs that reduce risk (protective stop, target, exit, flatten). They never pass through the entry checklist (spec P12-03 criterion 10); the reduce-only rule of P12-04 will answer here. The default refuses, so until then
/// nothing but an entry can pass the pre-send step, exactly as before this slice.
/// </summary>
public interface IExitSendPolicy
{
    Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken);
}

internal sealed class ClosedExitSendPolicy : IExitSendPolicy
{
    public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) =>
        Task.FromResult(new GateDecision(false, "no_exit_policy"));
}
