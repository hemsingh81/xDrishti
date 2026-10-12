using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// The primary Data account as the Dhan phase needs it: the account with the primary Data role, whether its connection (token) is usable, and the token itself
/// from the broker vault. The token is only handed to the caller, never logged or returned to the API.
/// </summary>
internal sealed class DataAccountStatus(IAccountStore accounts, IBrokerConnectionStore connections, IBrokerSecretVault vault, TimeProvider clock) : IDataAccountStatus
{
    public async ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.FindPrimaryDataAsync(cancellationToken);
        if (account is null)
        {
            return new DataAccountState(null, null, DataAccountStatuses.None, "No account has the primary Data role. Add or choose one under Accounts.");
        }

        var connection = (await connections.ListAsync([account.Id], cancellationToken)).GetValueOrDefault(account.Id);
        return (connection?.StatusAt(clock.GetUtcNow()) ?? ConnectionStatus.NotConnected) switch
        {
            ConnectionStatus.Connected => new DataAccountState(account.Id, account.Name, DataAccountStatuses.Connected, null),
            ConnectionStatus.Expired => new DataAccountState(account.Id, account.Name, DataAccountStatuses.Expired, $"The Dhan sign-in of the Data account '{account.Name}' has expired. Reconnect it under Accounts."),
            _ => new DataAccountState(account.Id, account.Name, DataAccountStatuses.NotConnected, $"The Data account '{account.Name}' is not connected to Dhan. Connect it under Accounts."),
        };
    }

    public async ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var state = await GetAsync(cancellationToken);
        return state is { CanFetch: true, AccountId: { } id } ? await vault.GetAsync(id, BrokerSecretName.AccessToken, cancellationToken) : null;
    }
}
