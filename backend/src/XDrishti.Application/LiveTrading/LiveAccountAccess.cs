using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.LiveTrading;

/// <summary>The saved Dhan token of a live account when its connection is usable (spec P11-01): the same vault and the same expiry the other reads use; the token goes nowhere but the request header.</summary>
internal sealed class LiveAccountAccess(IBrokerConnectionStore connections, IBrokerSecretVault vault, TimeProvider clock) : ILiveAccountAccess
{
    /// <summary>The token, or the reason there is none: <see cref="BrokerFailureKind.Unauthorized"/> when the token ran out, <see cref="BrokerFailureKind.Rejected"/> when the account has none.</summary>
    public async ValueTask<(SecretValue? Token, BrokerFailureKind? Reason)> TokenAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var all = await connections.ListAsync([accountId], cancellationToken);
        var status = all.GetValueOrDefault(accountId)?.StatusAt(clock.GetUtcNow()) ?? ConnectionStatus.NotConnected;
        if (status == ConnectionStatus.Expired)
        {
            return (null, BrokerFailureKind.Unauthorized);
        }

        if (status != ConnectionStatus.Connected)
        {
            return (null, BrokerFailureKind.Rejected);
        }

        var token = await vault.GetAsync(accountId, BrokerSecretName.AccessToken, cancellationToken);
        return token is null ? (null, BrokerFailureKind.Rejected) : (token, null);
    }
}

/// <summary>The port over <see cref="LiveAccountAccess"/>, so the use cases can be tested without a vault.</summary>
internal interface ILiveAccountAccess
{
    ValueTask<(SecretValue? Token, BrokerFailureKind? Reason)> TokenAsync(Guid accountId, CancellationToken cancellationToken);
}
