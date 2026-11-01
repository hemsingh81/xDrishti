using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Application.Confirm;

/// <summary>
/// Makes a rejected check run final (spec P12-03 amendment and criterion 7, P12-08 criterion 12, ADR 0166 amendment). For an intent in <c>checking</c> it reads the intent's account, the newest run, the newest ticket and the database clock in
/// one statement and asks <see cref="RejectionFinality"/>. A Soft-only rejection in <c>semi_auto</c> with an open ticket stays in <c>checking</c>; a confirmed ticket is left to <see cref="ConfirmHandOff"/>. The mode is read only when the run
/// has no Hard non-pass, so a mode source that fails or throws never delays a Hard rejection.
/// <para>
/// A final rejection never leaves an open card behind. Order: <b>close the card first, then move the intent.</b>
/// (1) When the newest ticket has no outcome it is closed: <c>expired</c> by the expiry path when the database clock is at or past its expiry (so no refusal exists that could unlock a second card), otherwise <c>refused</c> with reason
/// <c>intent_rejected</c>, which the issuer and the database treat as final (no second card follows it). The primary key on the outcome decides any race with the owner. A close that throws moves nothing (the intent stays <c>checking</c>
/// with its card, as before this step; the next pass tries again). A close that finds an outcome already stored re-reads and decides again: a confirmed card for the same Soft set is not final and nothing is moved.
/// (2) Only then <c>checking&gt;rejected</c> is appended with the reason, the run id and the codes. A successful close proves no confirmation of the newest card exists, so the hand-off cannot have moved the intent to <c>submitting</c>
/// through it. A move that fails (another writer moved first, or the store failed) leaves a closed card on an intent that the next pass judges again; the issuer gives it no new card.
/// (3) After the move, a card issued in between is closed too (the database serialises a ticket insert with the move on the intent's state row).
/// </para>
/// It holds no broker port, sends nothing, can only close a card or an intent, and is composed by no host. A composer runs the issuer first, then this step.
/// </summary>
public sealed partial class CheckRejectionMover(IRejectionReads reads, IConfirmTicketStore tickets, IModeSource modes, IExecEventStore events, TimeProvider clock, ILogger<CheckRejectionMover>? logger = null)
{
    public const string Actor = "system:check-reject";

    private readonly ILogger<CheckRejectionMover> _log = logger ?? NullLogger<CheckRejectionMover>.Instance;

    public async Task<RejectionStep> RunAsync(long intentId, CancellationToken cancellationToken)
    {
        if (await events.StateAsync(intentId, cancellationToken).ConfigureAwait(false) is not { State: ExecState.Checking } state)
        {
            return RejectionStep.None;
        }

        var (view, decision) = await DecideAsync(intentId, cancellationToken).ConfigureAwait(false);
        if (view is null || decision.Step != RejectionStep.RejectNow)
        {
            return decision.Step;
        }

        if (view.Ticket is { Outcome: null } open)
        {
            var closed = await CloseAsync(open, view.DbNow, cancellationToken).ConfigureAwait(false);
            if (closed is null)
            {
                return RejectionStep.None; // the card could not be closed: leave the intent as it was, never rejected with an open card
            }

            if (closed is false)
            {
                // Someone decided the card between the read and the close: judge again on what is stored now.
                (view, decision) = await DecideAsync(intentId, cancellationToken).ConfigureAwait(false);
                if (view is null)
                {
                    return RejectionStep.None;
                }

                if (decision.Step != RejectionStep.RejectNow)
                {
                    return decision.Step; // e.g. confirmed for the same Soft set: not final, the hand-off judges it
                }

                if (view.Ticket is { Outcome: null })
                {
                    return RejectionStep.None; // a newer open card appeared: the next pass closes it first
                }
            }
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["reason"] = decision.Reason, ["run"] = view.Run!.Id, ["codes"] = decision.Codes });
        bool movedOk;
        try
        {
            var moved = await events.AppendAsync(intentId, new ExecEvent(state.Seq + 1, ExecEventKind.State, ExecState.Checking, ExecState.Rejected, ExecEventSource.Engine, Actor, clock.GetUtcNow(), 0, payload), cancellationToken)
                .ConfigureAwait(false);
            movedOk = moved.IsSuccess;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            movedOk = false;
        }

        if (!movedOk)
        {
            return RejectionStep.None; // another writer moved it first, or the store failed: the closed card stays closed and gets no successor
        }

        await SweepAsync(intentId, cancellationToken).ConfigureAwait(false);
        var who = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(intentId)))[..8];
        LogRejected(_log, who, decision.Reason, view.Run.HardNonPass.Count, view.Run.SoftNonPass.Count);
        return RejectionStep.RejectNow;
    }

    private async Task<(RejectionView? View, RejectionDecision Decision)> DecideAsync(long intentId, CancellationToken cancellationToken)
    {
        RejectionView? view;
        try
        {
            view = await reads.ReadAsync(intentId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            view = null;
        }

        if (view is null)
        {
            return (null, new RejectionDecision(RejectionStep.None, RejectionReasons.NoRun, []));
        }

        // A Hard non-pass is final whatever the mode: do not let the mode source delay it.
        var mode = view.Run is { HardNonPass.Count: > 0 } ? null : await ModeAsync(view.AccountId, cancellationToken).ConfigureAwait(false);
        return (view, RejectionFinality.Decide(view.Run, mode, view.Ticket, view.DbNow));
    }

    private async Task<ModeAnswer?> ModeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            return await modes.AnswerAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // True when written, false when the card already had an outcome, null when it could not be written. A card past its expiry is closed expired (the expiry path), any other refused intent_rejected.
    private async Task<bool?> CloseAsync(RejectionTicket open, DateTimeOffset dbNow, CancellationToken cancellationToken)
    {
        try
        {
            if (dbNow >= open.ExpiresAt)
            {
                var expired = await tickets.ExpireDueAsync(TicketOutcomes.ExpiryActor, cancellationToken).ConfigureAwait(false);
                return expired.Any(c => c.TicketId == open.Id);
            }

            return await tickets.RefuseAsync(open.Id, RejectionReasons.IntentRejected, Actor, cancellationToken).ConfigureAwait(false) is { } written ? written : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // A card issued between the close and the move is closed as well. The database refuses any card once the intent is not checking, and its insert waits on the state row the move locks, so a card can only have been committed before the
    // move, and this read sees it.
    private async Task SweepAsync(long intentId, CancellationToken cancellationToken)
    {
        try
        {
            if (await reads.ReadAsync(intentId, cancellationToken).ConfigureAwait(false) is { Ticket: { Outcome: null } late })
            {
                await tickets.RefuseAsync(late.Id, RejectionReasons.IntentRejected, Actor, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogSweepFailed(_log);
        }
    }

    [LoggerMessage(EventId = 6635, Level = LogLevel.Information, Message = "execution.check_rejected intent={Intent} reason={Reason} hard={Hard} soft={Soft}")]
    private static partial void LogRejected(ILogger logger, string intent, string reason, int hard, int soft);

    [LoggerMessage(EventId = 6636, Level = LogLevel.Warning, Message = "execution.check_rejected_sweep_failed: a card issued during the rejection could not be closed; the expiry loop closes it, and the hand-off never moves a rejected intent")]
    private static partial void LogSweepFailed(ILogger logger);
}
