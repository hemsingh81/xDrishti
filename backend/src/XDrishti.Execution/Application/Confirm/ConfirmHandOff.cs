using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Confirm;

public enum HandOffResult
{
    /// <summary>Nothing moved: no confirmation yet (or the intent is not checking).</summary>
    Wait,

    /// <summary>The intent moved checking to submitting: the sender (P12-01/02) may now send its entry, once.</summary>
    Submitting,

    /// <summary>The confirmation is older than 30 seconds: the intent is cancelled <c>expired_after_confirm</c> and nothing is sent.</summary>
    Cancelled,
}

/// <summary>
/// The hand-off of a confirmed <c>semi_auto</c> entry (spec P12-08 criterion 12). For an intent in <c>checking</c> it asks <see cref="IConfirmGate"/>: open moves it to <c>submitting</c>; a confirmation older than 30 seconds moves it to
/// <c>cancelled</c> with reason <c>expired_after_confirm</c> and logs it for the owner; anything else moves nothing. Every move goes through the event chain, which the database judges again. It holds no broker port and is composed by no host:
/// the sender that would act on <c>submitting</c> is not composed with it.
/// </summary>
public sealed partial class ConfirmHandOff(IConfirmGate gate, IExecEventStore events, TimeProvider clock, ILogger<ConfirmHandOff>? logger = null)
{
    public const string Actor = "system:confirm-handoff";

    private readonly ILogger<ConfirmHandOff> _log = logger ?? NullLogger<ConfirmHandOff>.Instance;

    public async Task<HandOffResult> RunAsync(long intentId, CancellationToken cancellationToken)
    {
        if (await events.StateAsync(intentId, cancellationToken).ConfigureAwait(false) is not { State: ExecState.Checking } state)
        {
            return HandOffResult.Wait;
        }

        var decision = await gate.CheckAsync(intentId, cancellationToken).ConfigureAwait(false);
        var (to, reason, result) = decision switch
        {
            { Open: true } => (ExecState.Submitting, TicketOutcomes.Confirmed, HandOffResult.Submitting),
            { Reason: ConfirmPolicy.ExpiredAfterConfirm } => (ExecState.Cancelled, ConfirmPolicy.ExpiredAfterConfirm, HandOffResult.Cancelled),
            _ => (ExecState.Checking, decision.Reason, HandOffResult.Wait),
        };
        if (result == HandOffResult.Wait)
        {
            return HandOffResult.Wait;
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["reason"] = reason });
        var moved = await events.AppendAsync(intentId, new ExecEvent(state.Seq + 1, ExecEventKind.State, ExecState.Checking, to, ExecEventSource.Engine, Actor, clock.GetUtcNow(), 0, payload), cancellationToken).ConfigureAwait(false);
        if (moved.IsFailure)
        {
            return HandOffResult.Wait; // another writer moved it first; the chain refused this one
        }

        var into = to.ToString().ToLowerInvariant();
        var who = Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(intentId)))[..8];
        LogHandedOff(_log, who, into, reason);
        return result;
    }

    [LoggerMessage(EventId = 6719, Level = LogLevel.Warning, Message = "execution.confirm_handoff intent={Intent} to={To} reason={Reason}; expired_after_confirm sends nothing and is shown to the owner")]
    private static partial void LogHandedOff(ILogger logger, string intent, string to, string reason);
}
