using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

public enum SendStatus
{
    /// <summary>Refused before anything was recorded or sent (see the reason).</summary>
    Refused,

    /// <summary>Dropped after the write-ahead and before the request (a halt appeared).</summary>
    Dropped,

    /// <summary>The request went out; see <see cref="SendResult.Ack"/>.</summary>
    Sent,
}

public sealed record SendRequest(Guid AccountId, long IntentId, BrokerLeg Leg, DateTimeOffset RequestedAt);

public sealed record SendResult(SendStatus Status, string Reason, BrokerAck? Ack);

/// <summary>
/// The send protocol (P12-02). Order of effects: environment, account gate, halt (entries), pre-send re-check, age, rate; then the write-ahead record (committed), a second halt check for an entry, the one request, the outcome record.
/// Nothing here retries: an unknown outcome is resolved by <see cref="LegLookup"/> and a resend is a decision of the caller under <see cref="ResendPolicy"/>. Environment <c>live</c> is refused in this slice, whatever else is true.
/// </summary>
public sealed class LegSender(
    IBrokerOrders broker,
    IModeSource modes,
    IHaltSource halts,
    IPreSendCheck preSend,
    ISendJournal journal,
    ExecRateLimiter limiter,
    ExecEnvironment environment,
    TimeProvider clock,
    TimeSpan orderValidFor)
{
    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken)
    {
        if (environment != ExecEnvironment.Sandbox)
        {
            return Refuse(environment == ExecEnvironment.Live ? "live_not_enabled" : "environment_none");
        }

        var mode = await modes.AnswerAsync(request.AccountId, cancellationToken).ConfigureAwait(false);
        if (AccountGate.MayAct(mode, environment) is { Open: false } closed)
        {
            return Refuse(closed.Reason);
        }

        var isEntry = request.Leg.Kind == LegKind.Entry;
        if (isEntry && HaltPolicy.EntriesBlocked(await halts.ReadAsync(cancellationToken).ConfigureAwait(false)))
        {
            return Refuse("halted");
        }

        if (await preSend.CheckAsync(request.AccountId, request.IntentId, request.Leg, cancellationToken).ConfigureAwait(false) is { Open: false } denied)
        {
            return Refuse(denied.Reason);
        }

        if (clock.GetUtcNow() - request.RequestedAt > orderValidFor)
        {
            return Refuse("stale");
        }

        if (!limiter.TryAdmit())
        {
            return Refuse("rate_limited");
        }

        await journal.BeginAsync(request.AccountId, request.IntentId, request.Leg, cancellationToken).ConfigureAwait(false);

        if (isEntry && HaltPolicy.EntriesBlocked(await halts.ReadAsync(cancellationToken).ConfigureAwait(false)))
        {
            await journal.DroppedAsync(request.Leg, "halted_before_send", cancellationToken).ConfigureAwait(false);
            return new SendResult(SendStatus.Dropped, "halted_before_send", null);
        }

        BrokerAck ack;
        try
        {
            ack = await broker.PlaceAsync(request.Leg, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ack = new BrokerAck(BrokerOutcome.Unknown, null, "cancelled");
        }

        await journal.OutcomeAsync(request.Leg, ack, CancellationToken.None).ConfigureAwait(false);
        return new SendResult(SendStatus.Sent, ack.Reason ?? ack.Outcome.ToString(), ack);
    }

    private static SendResult Refuse(string reason) => new(SendStatus.Refused, reason, null);
}
