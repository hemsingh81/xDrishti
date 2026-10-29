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

/// <summary>One send. <c>ReferencePrice</c> is the live price the caller priced against (P12-03/04 supply it): with settings in force an exit or a flatten without one, or priced outside <c>exec.exit_collar_pct</c> of it, is refused.</summary>
public sealed record SendRequest(Guid AccountId, long IntentId, BrokerLeg Leg, DateTimeOffset RequestedAt, decimal? ReferencePrice = null);

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
    TimeSpan orderValidFor,
    ISendSettings? settings = null,
    ITokenGate? tokens = null,
    ISendHistory? history = null)
{
    // The host composition passes all three or none (tests of the bare protocol pass none): settings without the token gate or the history would be a half-guarded path.
    private readonly bool _guarded = (settings is not null && tokens is not null && history is not null)
        || (settings is null && tokens is null && history is null ? false : throw new ArgumentException("The send settings, the token gate and the send history are given together or not at all."));

    public async Task<SendResult> SendAsync(SendRequest request, CancellationToken cancellationToken)
    {
        if (environment != ExecEnvironment.Sandbox)
        {
            return Refuse(environment == ExecEnvironment.Live ? "live_not_enabled" : "environment_none");
        }

        var mode = await modes.AnswerAsync(request.AccountId, cancellationToken).ConfigureAwait(false);
        var reducesRisk = request.Leg.Kind is LegKind.Stop or LegKind.Exit or LegKind.Flatten;
        if ((reducesRisk ? ProtectionGate.MayProtect(mode, environment) : AccountGate.MayAct(mode, environment)) is { Open: false } closed)
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

        SendSettings? current = null;
        if (_guarded)
        {
            current = settings!.Current();
            if (current is null)
            {
                return Refuse("settings_unavailable");
            }

            if (await tokens!.CheckAsync(request.AccountId, current.TokenMinRemainingMinutes, cancellationToken).ConfigureAwait(false) is { Open: false } token)
            {
                return Refuse(token.Reason);
            }

            if (request.Leg.Kind is LegKind.Exit or LegKind.Flatten && OutsideCollar(request, current.ExitCollarPct) is { } collar)
            {
                return Refuse(collar);
            }
        }

        if (clock.GetUtcNow() - request.RequestedAt > (current?.OrderValidFor ?? orderValidFor))
        {
            return Refuse("stale");
        }

        if (_guarded && !await SeedLimiterAsync(cancellationToken).ConfigureAwait(false))
        {
            return Refuse("send_history_unavailable");
        }

        if (!(current is null ? limiter.TryAdmit() : limiter.TryAdmit(current.PerSecond, current.PerMinute, current.PerDay)))
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

    private static string? OutsideCollar(SendRequest request, decimal collarPct) =>
        request.ReferencePrice is not { } reference || reference <= 0m
            ? "no_reference_price"
            : Math.Abs(request.Leg.Price - reference) / reference * 100m > collarPct ? "outside_collar" : null;

    // Loads the write-ahead rows of the last day once per process; a read that fails is a refusal (and is tried again at the next send), never an empty history.
    private async Task<bool> SeedLimiterAsync(CancellationToken cancellationToken)
    {
        if (limiter.IsSeeded)
        {
            return true;
        }

        try
        {
            limiter.Seed(await history!.BeginsSinceAsync(clock.GetUtcNow() - TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false));
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    private static SendResult Refuse(string reason) => new(SendStatus.Refused, reason, null);
}
