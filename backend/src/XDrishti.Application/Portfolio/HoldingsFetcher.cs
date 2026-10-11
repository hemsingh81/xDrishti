using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>What Dhan reported (one row per holding) and the instruments those rows map to, by <see cref="BrokerHolding.Key"/>.</summary>
internal sealed record FetchedHoldings(IReadOnlyList<BrokerHolding> Rows, IReadOnlyDictionary<string, InstrumentRef> Instruments);

/// <summary>
/// The steps every holdings use case shares: the account must exist and have the Portfolio role on, be connected to Dhan with a usable token, and
/// then Dhan's holdings are fetched and mapped to the instrument master. Failures come back as results with stable codes.
/// </summary>
internal sealed class HoldingsFetcher(
    IAccountStore accounts,
    IBucketStore buckets,
    IBrokerConnectionStore connections,
    IBrokerSecretVault vault,
    IBrokerHoldings broker,
    IInstrumentMapper mapper,
    TimeProvider clock)
{
    /// <summary>The active account, which must have the Portfolio role on.</summary>
    public async ValueTask<Result<BrokerAccount>> AccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<BrokerAccount>(AccountErrors.NotFound);
        }

        return account.Portfolio.Enabled ? Result.Success(account) : Result.Failure<BrokerAccount>(PortfolioErrors.RoleOff);
    }

    /// <summary>The bucket new holdings go to: the custom bucket chosen in the account's portfolio settings, otherwise its own default bucket.</summary>
    public async ValueTask<Result<Guid>> BucketAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        if (account.Portfolio.DefaultBucketId is { } chosen)
        {
            return Result.Success(chosen);
        }

        var own = await buckets.FindDefaultForAccountAsync(account.Id, cancellationToken);
        return own is { ArchivedAt: null } ? Result.Success(own.Id) : Result.Failure<Guid>(PortfolioErrors.NoBucket);
    }

    /// <summary>Success when the account has a connection that has not expired.</summary>
    public async ValueTask<Result> ConnectedAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var all = await connections.ListAsync([accountId], cancellationToken);
        return all.GetValueOrDefault(accountId)?.StatusAt(clock.GetUtcNow()) == ConnectionStatus.Connected
            ? Result.Success()
            : Result.Failure(PortfolioErrors.NotConnected);
    }

    /// <summary>Dhan's holdings of the account, merged to one row per holding and mapped to the master list.</summary>
    public async ValueTask<Result<FetchedHoldings>> FetchAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var connected = await ConnectedAsync(accountId, cancellationToken);
        if (connected.IsFailure)
        {
            return Result.Failure<FetchedHoldings>(connected.Error);
        }

        var token = await vault.GetAsync(accountId, BrokerSecretName.AccessToken, cancellationToken);
        if (token is null)
        {
            return Result.Failure<FetchedHoldings>(PortfolioErrors.NotConnected);
        }

        var response = await broker.FetchAsync(token, cancellationToken);
        if (!response.IsSuccess)
        {
            var failure = response.Failure!;
            return Result.Failure<FetchedHoldings>(failure.Kind == BrokerFailureKind.Unauthorized ? PortfolioErrors.TokenRefused : ConnectionErrors.Broker(failure));
        }

        var rows = BrokerHolding.Merge(response.Value);
        var instruments = rows.Count == 0 ? new Dictionary<string, InstrumentRef>() : await mapper.MapAsync(rows, cancellationToken);
        return Result.Success(new FetchedHoldings(rows, instruments));
    }
}
