namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>
/// Dhan's authentication API (ADR 0008, 0009): starting and completing the browser consent login. Only Dhan's generate-consent and
/// consumeApp-consent endpoints are ever called. Implementations never log, return or put into exceptions any secret, token or
/// code, and report failures as <see cref="BrokerResponse{T}"/> instead of throwing.
/// </summary>
public interface IBrokerConnector
{
    /// <summary>Asks Dhan for a consent and returns the Dhan login page to send the owner to.</summary>
    ValueTask<BrokerResponse<Uri>> StartLoginAsync(LoginStartRequest request, CancellationToken cancellationToken);

    /// <summary>Exchanges the <c>tokenId</c> Dhan sent back to the redirect URL for an access token.</summary>
    ValueTask<BrokerResponse<BrokerToken>> CompleteLoginAsync(LoginCompletion completion, CancellationToken cancellationToken);
}
