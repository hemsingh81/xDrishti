using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Application.Confirm;

/// <summary>The codes of a hand-over that stores no ticket. Short words; never a price, a name or an account.</summary>
public static class IssueRefusals
{
    public const string SettingsUnavailable = "execution.settings_unavailable";
    public const string IntentUnknown = "execution.not_found";
    public const string NotChecking = "execution.intent_not_checking";
    public const string NoTapNeeded = "execution.no_ticket_needed";
    public const string ContextUnavailable = "execution.context_unavailable";
    public const string CheckMissing = "execution.check_missing";
    public const string CheckRejected = "execution.check_rejected";
    public const string ApprovalMissing = "execution.approval_missing";
    public const string CanaryQuantity = "execution.canary_quantity";
    public const string LevelsInvalid = "execution.levels_invalid";
    public const string TextTooLong = "execution.text_too_long";
    public const string Window = "execution.window";
    public const string StoreRefused = "execution.ticket_refused";
}

/// <summary>
/// Issues the confirm ticket of a <c>semi_auto</c> entry (spec P12-08 criteria 1, 2, 5, 9, 13). Given an intent in <c>checking</c> whose newest full P12-03 run has every Hard check passed (cleared, or rejected by Soft checks only), it stores
/// one ticket with the exact text and its hash, the entry's client order id (P12-01, attempt 1), the last price as reference, the notional, the worst case at the stop, the password rule and the Soft codes, living
/// <c>exec.confirm_ttl_s</c>. A second hand-over returns the intent's current ticket; after a refused ticket, once, while the entry window is open, a new ticket with new text is issued (at most two). A fourth open ticket of an account is
/// stored closed <c>refused</c> (<c>too_many_open</c>) and never reaches the owner. It writes only <c>execution.confirm_tickets</c> and <c>ticket_outcomes</c> (refused); it holds no broker port, sends nothing and is composed by no host:
/// no account can be <c>semi_auto</c> yet (ADR 0163).
/// </summary>
public sealed partial class ConfirmTicketIssuer(
    IConfirmTicketStore store, IModeSource modes, IIntentQuotes quotes, IIntentSettings intentSettings, IConfirmSettings confirmSettings, ExecEnvironment environment, TimeProvider clock, ILogger<ConfirmTicketIssuer>? logger = null)
{
    private readonly ILogger<ConfirmTicketIssuer> _log = logger ?? NullLogger<ConfirmTicketIssuer>.Instance;

    /// <summary>The intent's current ticket (open, decided, or newly issued), or the reason none can be stored.</summary>
    public async Task<Result<StoredTicket>> IssueAsync(long intentId, CancellationToken cancellationToken)
    {
        if (confirmSettings.Current() is not { } settings || intentSettings.Current() is not { } window)
        {
            return Refuse(IssueRefusals.SettingsUnavailable, null);
        }

        if (await store.IntentAsync(intentId, cancellationToken).ConfigureAwait(false) is not { } intent)
        {
            return Refuse(IssueRefusals.IntentUnknown, null);
        }

        var tickets = await store.TicketsAsync(intent.PublicId, cancellationToken).ConfigureAwait(false);
        var newest = tickets.Count > 0 ? tickets[^1] : null;
        if (newest is not null && (newest.Outcome != TicketOutcomes.Refused || tickets.Count >= ConfirmPolicy.MaxAttempts))
        {
            return Result.Success(newest); // the same hand-over again: the open or decided ticket, never a third
        }

        var now = clock.GetUtcNow().ToUniversalTime();
        var refusal = await PreconditionAsync(intent, window, now, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            return newest is null ? Refuse(refusal, intent) : Result.Success(newest);
        }

        if (await store.ContextAsync(intent, cancellationToken).ConfigureAwait(false) is not { } context)
        {
            return Refuse(IssueRefusals.ContextUnavailable, intent);
        }

        if (context.Run is not { } run)
        {
            return Refuse(IssueRefusals.CheckMissing, intent);
        }

        if (run.HardNonPass > 0)
        {
            return Refuse(IssueRefusals.CheckRejected, intent);
        }

        if (context.ApprovedAt is not { } approvedAt)
        {
            return Refuse(IssueRefusals.ApprovalMissing, intent);
        }

        if (await quotes.LastAsync(intent.Symbol, cancellationToken).ConfigureAwait(false) is not { } price || !IntentRules.IsFresh(price, now, window.QuoteMaxAge))
        {
            return Refuse(ConfirmCodes.PriceUnavailable, intent);
        }

        var issuedAt = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        var expiresAt = issuedAt + settings.Ttl;
        var parameters = new ConfirmTicketParams(
            "BUY", intent.Quantity, Display(intent.Symbol), intent.Entry, intent.Stop, intent.Target, context.Broker, context.AccountName,
            new DateTimeOffset(approvedAt.UtcTicks - (approvedAt.UtcTicks % TimeSpan.TicksPerMinute), TimeSpan.Zero), expiresAt, window.CanaryPending);
        var text = ConfirmText.Render(parameters);
        if (text.Length > ConfirmText.MaxLength)
        {
            return Refuse(IssueRefusals.TextTooLong, intent);
        }

        var coid = ClientOrderId.Create(intent.Session, intent.IntentId, LegKind.Entry, 1);
        if (coid.IsFailure)
        {
            return Refuse(IssueRefusals.LevelsInvalid, intent);
        }

        var password = ConfirmPolicy.Password(parameters.Notional, context.Capital, settings.StepUpNotionalPct, run.SoftNonPass.Count > 0);
        var tooMany = await store.OpenCountAsync(intent.AccountId, cancellationToken).ConfigureAwait(false) >= ConfirmPolicy.MaxOpenPerAccount;
        var draft = new TicketDraft(
            intent, tickets.Count + 1, parameters, text, ConfirmText.Hash(text), coid.Value.ToString(), price.Last, password, run.SoftNonPass, run.Id, tooMany ? ConfirmCodes.Reason(ConfirmCodes.TooManyOpen) : null);
        var stored = await store.InsertAsync(draft, issuedAt, expiresAt, cancellationToken).ConfigureAwait(false);
        if (stored.Ticket is { } ticket)
        {
            var (t, i, a) = (Hash(ticket.Id), Hash(intent.PublicId), Hash(intent.AccountId));
            var state = ticket.Outcome ?? TicketOutcomes.Open;
            LogIssued(_log, t, i, a, ticket.Attempt, ticket.RequiresPassword, state);
            return Result.Success(ticket);
        }

        if (stored.Refused is not null)
        {
            return Refuse(IssueRefusals.StoreRefused, intent);
        }

        // Another hand-over stored this attempt first: answer with it.
        var again = await store.TicketsAsync(intent.PublicId, cancellationToken).ConfigureAwait(false);
        return again.Count > 0 ? Result.Success(again[^1]) : Refuse(IssueRefusals.StoreRefused, intent);
    }

    // The intent is checking, the account still routes an entry to the owner's tap, the entry window is open and the quantity respects the canary.
    private async Task<string?> PreconditionAsync(TicketIntent intent, IntentSettings window, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (intent.State != ExecState.Checking)
        {
            return IssueRefusals.NotChecking;
        }

        var answer = await modes.AnswerAsync(intent.AccountId, cancellationToken).ConfigureAwait(false);
        if (AccountGate.MayAct(answer, environment) is { Open: false })
        {
            return ConfirmCodes.ModeChanged;
        }

        var route = ModeRouting.Decide(answer!.EffectiveMode, ActionClass.Entry, intent.Side);
        if (route.Kind != RouteKind.AwaitConfirm)
        {
            return route.Kind == RouteKind.Automatic ? IssueRefusals.NoTapNeeded : route.Code ?? ConfirmCodes.ModeChanged;
        }

        if (!IntentRules.InWindow(now, intent.Session, window))
        {
            return IssueRefusals.Window;
        }

        if (window.CanaryPending && intent.Quantity > 1)
        {
            return IssueRefusals.CanaryQuantity;
        }

        return intent.Stop > 0 && intent.Stop < intent.Entry && intent.Target > intent.Entry ? null : IssueRefusals.LevelsInvalid;
    }

    private Result<StoredTicket> Refuse(string code, TicketIntent? intent)
    {
        var who = intent is null ? "-" : Hash(intent.PublicId);
        LogNotIssued(_log, who, code);
        return Result.Failure<StoredTicket>(Error.Conflict(code, "No confirm ticket was issued: " + code + "."));
    }

    private static string Display(string symbol) => symbol.StartsWith("NSE:", StringComparison.Ordinal) ? symbol[4..] : symbol;

    private static string Hash(Guid id) => Convert.ToHexStringLower(SHA256.HashData(id.ToByteArray()))[..8];

    [LoggerMessage(EventId = 6716, Level = LogLevel.Information, Message = "execution.ticket_issued ticket={Ticket} intent={Intent} account={Account} attempt={Attempt} password={Password} state={State}")]
    private static partial void LogIssued(ILogger logger, string ticket, string intent, string account, int attempt, bool password, string state);

    [LoggerMessage(EventId = 6717, Level = LogLevel.Information, Message = "execution.ticket_not_issued intent={Intent} code={Code}")]
    private static partial void LogNotIssued(ILogger logger, string intent, string code);
}
