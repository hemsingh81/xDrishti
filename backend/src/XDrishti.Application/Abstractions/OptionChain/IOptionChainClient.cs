namespace XDrishti.Application.Abstractions.OptionChain;

/// <summary>
/// Asks Dhan for an expiry list or a chain with the Data account's token (P14-02). Every request of the process waits so that two are never closer than <c>spacing</c> (and never closer than <see cref="OptionChainSettings.MinSpacing"/>), across both kinds.
/// Never retries; never logs the URL, the token, the client id or the body; maps a failure to an outcome word instead of throwing.
/// </summary>
public interface IOptionChainClient
{
    ValueTask<ChainReply> SendAsync(ChainRequest request, TimeSpan spacing, CancellationToken cancellationToken);
}
