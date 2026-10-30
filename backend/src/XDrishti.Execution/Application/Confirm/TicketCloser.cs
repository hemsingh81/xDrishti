using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Confirm;

/// <summary>
/// The system's two ways to end a ticket (spec P12-08 criteria 5 and 11), both the safe direction: <see cref="ExpireDueAsync"/> closes every unanswered ticket past its expiry <c>expired</c> as <c>system:confirm-expiry</c> (the host runs it every
/// 2 seconds, so a ticket is closed within 5 seconds of its expiry), and <see cref="RefuseAsync"/> closes one ticket <c>refused</c> when the owner's tap was refused for its price or its Soft set (the API asks through a command row). The
/// primary key of the outcome decides any race with the owner's tap. It sends nothing.
/// </summary>
public sealed partial class TicketCloser(IConfirmTicketStore store, ILogger<TicketCloser>? logger = null)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly ILogger<TicketCloser> _log = logger ?? NullLogger<TicketCloser>.Instance;

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var closed = await store.ExpireDueAsync(TicketOutcomes.ExpiryActor, cancellationToken).ConfigureAwait(false);
        foreach (var c in closed)
        {
            var (ticket, intent, account) = (Hash(c.TicketId), Hash(c.IntentPublicId), Hash(c.AccountId));
            LogClosed(_log, ticket, intent, account, c.Outcome, c.Reason, c.MillisFromIssue);
        }

        return closed.Count;
    }

    /// <summary>
    /// Closes the ticket <c>refused</c> with the short reason of <paramref name="code"/>, which must be a refusal that closes a ticket (price moved, price unavailable, Soft set changed). True when written, false when the ticket already had an
    /// outcome, null when the code is not one of those or there is no such ticket.
    /// </summary>
    public async Task<bool?> RefuseAsync(Guid ticketId, string code, CancellationToken cancellationToken)
    {
        if (!ConfirmPolicy.ClosesTicket(code))
        {
            return null;
        }

        var reason = ConfirmCodes.Reason(code);
        var written = await store.RefuseAsync(ticketId, reason, TicketOutcomes.TicketActor, cancellationToken).ConfigureAwait(false);
        if (written is true)
        {
            var ticket = Hash(ticketId);
            LogClosed(_log, ticket, "-", "-", TicketOutcomes.Refused, reason, -1);
        }

        return written;
    }

    private static string Hash(Guid id) => Convert.ToHexStringLower(SHA256.HashData(id.ToByteArray()))[..8];

    [LoggerMessage(EventId = 6718, Level = LogLevel.Information, Message = "execution.ticket_closed ticket={Ticket} intent={Intent} account={Account} outcome={Outcome} reason={Reason} ms={Millis}")]
    private static partial void LogClosed(ILogger logger, string ticket, string intent, string account, string outcome, string reason, long millis);
}
