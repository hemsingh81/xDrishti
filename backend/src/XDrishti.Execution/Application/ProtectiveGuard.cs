using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>The gate for protective actions that do not go through <see cref="LegSender"/> (a cancel, a stop change): environment sandbox (live is refused, none is refused) and <see cref="ProtectionGate"/> on the account's answer. No answer is a refusal.</summary>
public sealed class ProtectiveGuard(IModeSource modes, ExecEnvironment environment)
{
    public async Task<GateDecision> OpenAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (environment != ExecEnvironment.Sandbox)
        {
            return new GateDecision(false, environment == ExecEnvironment.Live ? "live_not_enabled" : "environment_none");
        }

        return ProtectionGate.MayProtect(await modes.AnswerAsync(accountId, cancellationToken).ConfigureAwait(false), environment);
    }
}
