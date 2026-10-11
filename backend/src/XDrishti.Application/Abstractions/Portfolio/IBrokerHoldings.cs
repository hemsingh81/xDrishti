using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The holdings of a broker account (ADR 0012): quantity, average cost and last price of every delivery holding, without purchase dates. Only
/// reads. Implementations never log, return or put into exceptions the token or the broker's raw answer, and report failures as
/// <see cref="BrokerResponse{T}"/> instead of throwing. A token the broker refuses is <see cref="BrokerFailureKind.Unauthorized"/>.
/// </summary>
public interface IBrokerHoldings
{
    /// <summary>The account's holdings, one entry per broker row (not merged). An account without holdings gives an empty list.</summary>
    ValueTask<BrokerResponse<IReadOnlyList<BrokerHolding>>> FetchAsync(SecretValue accessToken, CancellationToken cancellationToken);
}
