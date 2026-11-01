namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>
/// Asks Dhan for one window of expired-option bars with the Data account's token (P14-03). Every request of the process waits so that two are never closer than <c>spacing</c> (and never closer than <see cref="OptionBackfillSettings.MinSpacing"/>).
/// Never retries; never logs the URL, the token, the client id or the body; maps a failure to an outcome word instead of throwing.
/// </summary>
public interface IOptionBackfillClient
{
    ValueTask<BackfillReply> SendAsync(RollingRequest request, TimeSpan spacing, CancellationToken cancellationToken);
}
