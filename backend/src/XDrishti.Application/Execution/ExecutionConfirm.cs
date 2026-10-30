using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Execution;

/// <summary>
/// One confirm ticket as the owner's card shows it (spec P12-08 Contract). <c>Text</c> is shown verbatim and never re-typed; <c>ServerNow</c> is the database clock at the read, so the countdown is the server's. <c>State</c> is
/// <c>open</c>, <c>confirmed</c>, <c>skipped</c>, <c>expired</c> or <c>refused</c> (a ticket past its expiry without an outcome reads <c>expired</c> before the loop writes it). <c>SoftCodes</c> lists the Soft checks not passed at issue: a
/// confirm then needs a reason.
/// </summary>
public sealed record ConfirmTicketView(
    Guid Id,
    Guid AccountId,
    string AccountName,
    long PlanItemId,
    string Text,
    string TextHash,
    string Kind,
    string ClientOrderId,
    decimal RefPrice,
    decimal Notional,
    decimal RiskAtStop,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ServerNow,
    bool RequiresPassword,
    IReadOnlyList<string> PasswordReasons,
    IReadOnlyList<string> SoftCodes,
    string State,
    string? OutcomeReason,
    DateTimeOffset? DecidedAt);

/// <summary>The answer to a tap or a skip. <c>AlreadyDecided</c> is true when an earlier request decided the ticket (the same decision, the same <c>DecidedAt</c>).</summary>
public sealed record ConfirmResultView(Guid TicketId, string Outcome, bool AlreadyDecided, DateTimeOffset DecidedAt);

/// <summary>The owner's unlock of one account for one India session date, valid until 15:30 India time.</summary>
public sealed record DayUnlockView(long Id, Guid AccountId, DateOnly SessionDate, DateTimeOffset UnlockedAt, DateTimeOffset ExpiresAt);

/// <summary>A stored ticket with its account name and outcome, as the runtime role reads it.</summary>
public sealed record ConfirmTicketRow(
    Guid Id,
    Guid AccountId,
    string AccountName,
    Guid IntentPublicId,
    long PlanItemId,
    DateOnly SessionDate,
    string Kind,
    string Text,
    string TextHash,
    string ClientOrderId,
    decimal RefPrice,
    decimal Notional,
    decimal RiskAtStop,
    bool RequiresPassword,
    IReadOnlyList<string> PasswordReasons,
    IReadOnlyList<string> SoftCodes,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string? Outcome,
    string? OutcomeReason,
    DateTimeOffset? DecidedAt);

/// <summary>What the tap is judged on beyond the state: the last price and its time, the Soft codes of the intent's newest full run now (null when there is no run) and the confirm settings in force.</summary>
public sealed record ConfirmFacts(ConfirmState State, decimal? LastPrice, DateTimeOffset? LastPriceAt, IReadOnlyList<string>? SoftNow, ConfirmSettings Settings);

/// <summary>The result of an outcome insert: written (with the database's time), a duplicate (another request decided first), or the database's refusal code.</summary>
public sealed record OutcomeWrite(DateTimeOffset? DecidedAt, bool Duplicate, string? Refused);

/// <summary>The confirm record as the runtime role: SELECT on the tickets, INSERT of the owner's two outcomes (the trigger refuses anything else). One reader, one writer, the database clock.</summary>
public interface IConfirmTicketAccess
{
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfirmTicketRow>> ListAsync(DateOnly session, bool openOnly, CancellationToken cancellationToken);

    Task<ConfirmTicketRow?> FindAsync(Guid ticketId, CancellationToken cancellationToken);

    /// <summary>The facts of the tap in one read. A fact that cannot be read is null (it refuses).</summary>
    Task<ConfirmFacts> FactsAsync(ConfirmTicketRow ticket, CancellationToken cancellationToken);

    Task<OutcomeWrite> InsertOutcomeAsync(Guid ticketId, string outcome, string reason, string actor, string? note, CancellationToken cancellationToken);
}

/// <summary>The day unlocks as the runtime role: SELECT, and INSERT of the owner's unlock (the database stamps the time and the expiry).</summary>
public interface IDayUnlockAccess
{
    Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DayUnlockView>> ListAsync(DateOnly session, CancellationToken cancellationToken);

    /// <summary>The account's effective execution mode, or null when there is no such account.</summary>
    Task<string?> EffectiveModeAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The stored unlock (a second one for the same account and date answers the first), or the database's refusal code.</summary>
    Task<(DayUnlockView? Row, string? Refused)> InsertAsync(Guid accountId, DateOnly session, string actor, CancellationToken cancellationToken);
}

internal static partial class ConfirmLog
{
    public static string Hash(Guid id) => Convert.ToHexStringLower(SHA256.HashData(id.ToByteArray()))[..8];

    [LoggerMessage(EventId = 6761, Level = LogLevel.Information, Message = "execution.ticket_decided ticket={Ticket} intent={Intent} account={Account} outcome={Outcome} reason={Reason} ms={Millis}")]
    public static partial void Decided(ILogger logger, string ticket, string intent, string account, string outcome, string reason, long millis);

    [LoggerMessage(EventId = 6762, Level = LogLevel.Information, Message = "execution.confirm_refused ticket={Ticket} intent={Intent} account={Account} code={Code}")]
    public static partial void Refused(ILogger logger, string ticket, string intent, string account, string code);

    [LoggerMessage(EventId = 6763, Level = LogLevel.Information, Message = "execution.day_unlocked account={Account}")]
    public static partial void Unlocked(ILogger logger, string account);

    [LoggerMessage(EventId = 6764, Level = LogLevel.Information, Message = "execution.day_unlock_refused code={Code}")]
    public static partial void UnlockRefused(ILogger logger, string code);
}

/// <summary>The tickets of a session (default today in India): <c>open</c> (default) or <c>all</c>. Read only.</summary>
public sealed record GetConfirmTicketsQuery(string? State, DateOnly? Date) : IQuery<Result<IReadOnlyList<ConfirmTicketView>>>;

internal sealed class GetConfirmTicketsHandler(IConfirmTicketAccess access) : IQueryHandler<GetConfirmTicketsQuery, Result<IReadOnlyList<ConfirmTicketView>>>
{
    public async ValueTask<Result<IReadOnlyList<ConfirmTicketView>>> HandleAsync(GetConfirmTicketsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.State is not (null or "open" or "all"))
        {
            return Result.Failure<IReadOnlyList<ConfirmTicketView>>(Error.Validation(ConfirmCodes.ValuesInvalid, "state is open or all."));
        }

        var now = await access.NowAsync(cancellationToken);
        var open = query.State != "all";
        var rows = await access.ListAsync(query.Date ?? IndiaTime.Today(now), open, cancellationToken);
        return Result.Success<IReadOnlyList<ConfirmTicketView>>([.. rows.Select(r => ConfirmViews.View(r, now)).Where(v => !open || v.State == TicketOutcomes.Open)]);
    }
}

internal static class ConfirmViews
{
    public static ConfirmTicketView View(ConfirmTicketRow r, DateTimeOffset now)
    {
        var state = r.Outcome ?? (ConfirmPolicy.IsExpired(now, r.ExpiresAt) ? TicketOutcomes.Expired : TicketOutcomes.Open);
        var reason = r.Outcome is null && state == TicketOutcomes.Expired ? TicketOutcomes.TtlReason : r.OutcomeReason;
        return new(
            r.Id, r.AccountId, r.AccountName, r.PlanItemId, r.Text, r.TextHash, r.Kind, r.ClientOrderId, r.RefPrice, r.Notional, r.RiskAtStop, r.IssuedAt, r.ExpiresAt, now, r.RequiresPassword, r.PasswordReasons, r.SoftCodes, state,
            reason, r.DecidedAt);
    }

    public static Result<ConfirmResultView> Decided(ConfirmTicketRow ticket, string expected)
    {
        if (ticket.Outcome == expected && ticket.DecidedAt is { } at)
        {
            return Result.Success(new ConfirmResultView(ticket.Id, expected, true, at));
        }

        return Result.Failure<ConfirmResultView>(Error.Conflict(ConfirmCodes.TicketClosed, "The ticket is already closed: " + ticket.Outcome + " (" + ticket.OutcomeReason + "). Nothing was sent."));
    }
}

/// <summary>
/// The owner's tap (spec P12-08 criteria 3 to 10). The endpoint checked the session and the CSRF token and built the actor from the session; the password, when the ticket asks for it, is checked here through the step-up handler (it
/// counts toward the lockout). The browser echoes the hash of the text it showed; the reason is required when the ticket lists Soft codes.
/// </summary>
public sealed record ConfirmTicketCommand(string Actor, Guid SessionId, ClientInfo Client, Guid TicketId, string? TextHash, string? Password, string? Reason) : ICommand;

/// <summary>
/// Judges the tap and, when everything holds, inserts the one <c>confirmed</c> outcome; the database trigger judges it again by its own clock, so the browser's clock never decides. A ticket already decided answers that decision
/// (<c>alreadyDecided</c>) or <c>execution.ticket_closed</c>; a changed text stores nothing; a state refusal leaves the ticket open; a price or Soft-set refusal asks the execution service to close it <c>refused</c>. It sends nothing:
/// the execution service reads the outcome (P12-01/02).
/// </summary>
internal sealed class ConfirmTicketHandler(
    IConfirmTicketAccess access, IExecutionCommands commands, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, ILogger<ConfirmTicketHandler> logger) : ICommandHandler<ConfirmTicketCommand, ConfirmResultView>
{
    public async ValueTask<Result<ConfirmResultView>> HandleAsync(ConfirmTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TicketOutcomes.IsOwnerActor(command.Actor))
        {
            return Result.Failure<ConfirmResultView>(Error.Forbidden(ConfirmCodes.OwnerOnly, "Only the signed-in owner can confirm a ticket."));
        }

        if (await access.FindAsync(command.TicketId, cancellationToken) is not { } ticket)
        {
            return Result.Failure<ConfirmResultView>(Error.NotFound(ConfirmCodes.NotFound, "There is no such ticket."));
        }

        if (ticket.Outcome is not null)
        {
            return ConfirmViews.Decided(ticket, TicketOutcomes.Confirmed);
        }

        if (!string.Equals(command.TextHash, ticket.TextHash, StringComparison.Ordinal))
        {
            return Refuse(ticket, ConfirmCodes.TextChanged, "The text you saw is not the ticket's text. Reload the card; nothing was stored or sent.");
        }

        var facts = await access.FactsAsync(ticket, cancellationToken);
        if (ConfirmPolicy.StateRefusal(facts.State) is { } state)
        {
            return Refuse(ticket, state, StateMessage(state));
        }

        var reason = command.Reason?.Trim();
        if (ticket.SoftCodes.Count > 0 && !ConfirmPolicy.ReasonOk(reason))
        {
            return Result.Failure<ConfirmResultView>(Error.Validation(ConfirmCodes.ReasonRequired, "Soft checks did not pass (" + string.Join(", ", ticket.SoftCodes) + "): give a reason of 1 to 200 characters."));
        }

        if (ticket.RequiresPassword)
        {
            if (string.IsNullOrEmpty(command.Password))
            {
                return Result.Failure<ConfirmResultView>(Error.Validation(ConfirmCodes.PasswordRequired, "This ticket needs your password with the tap (" + string.Join(", ", ticket.PasswordReasons) + ")."));
            }

            var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
            if (checkedPassword.IsFailure)
            {
                return Result.Failure<ConfirmResultView>(checkedPassword.Error);
            }
        }

        var price = ConfirmPolicy.PriceRefusal(ticket.RefPrice, facts.LastPrice, facts.LastPriceAt, facts.State.Now, facts.Settings);
        var refusal = price ?? (ConfirmPolicy.SameSoftSet(ticket.SoftCodes, facts.SoftNow ?? []) ? null : ConfirmCodes.SoftSetChanged);
        if (refusal is not null)
        {
            // The API may not write a refused outcome; the execution service closes the ticket and may issue the one new ticket.
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["ticketId"] = ticket.Id.ToString(), ["code"] = refusal });
            await commands.EnqueueAsync("ticket_refuse", ticket.AccountId, payload, command.Actor, cancellationToken);
            return Refuse(ticket, refusal, StateMessage(refusal));
        }

        var written = await access.InsertOutcomeAsync(ticket.Id, TicketOutcomes.Confirmed, TicketOutcomes.TapReason, command.Actor, ticket.SoftCodes.Count > 0 ? reason : null, cancellationToken);
        if (written.Refused is { } code)
        {
            return Refuse(ticket, code, StateMessage(code));
        }

        if (written.Duplicate || written.DecidedAt is not { } decidedAt)
        {
            return await access.FindAsync(ticket.Id, cancellationToken) is { } now
                ? ConfirmViews.Decided(now, TicketOutcomes.Confirmed)
                : Result.Failure<ConfirmResultView>(Error.NotFound(ConfirmCodes.NotFound, "There is no such ticket."));
        }

        var (t, i, a) = (ConfirmLog.Hash(ticket.Id), ConfirmLog.Hash(ticket.IntentPublicId), ConfirmLog.Hash(ticket.AccountId));
        var millis = (long)(decidedAt - ticket.IssuedAt).TotalMilliseconds;
        ConfirmLog.Decided(logger, t, i, a, TicketOutcomes.Confirmed, TicketOutcomes.TapReason, millis);
        return Result.Success(new ConfirmResultView(ticket.Id, TicketOutcomes.Confirmed, false, decidedAt));
    }

    internal static string StateMessage(string code) => code switch
    {
        ConfirmCodes.TicketExpired => "The card expired before the tap (the server's clock decides). Nothing was sent.",
        ConfirmCodes.Disabled => "Execution is switched off (execution_enabled is OFF). Nothing was sent.",
        ConfirmCodes.ModeChanged => "The account is no longer in semi_auto. Nothing was sent.",
        ConfirmCodes.Halted => "A day halt or the kill switch is in force. Nothing was sent.",
        ConfirmCodes.NotReady => "The account is not ready (login, token or static IP). Nothing was sent.",
        ConfirmCodes.NotUnlocked => "The day is not unlocked for this account: unlock it with your password first. Nothing was sent.",
        ConfirmCodes.PriceUnavailable => "There is no fresh last price to compare with. The ticket is closed; nothing was sent.",
        ConfirmCodes.PriceMoved => "The price moved beyond the limit since the card was issued. The ticket is closed; nothing was sent.",
        ConfirmCodes.SoftSetChanged => "The Soft checks changed since the card was issued. The ticket is closed; nothing was sent.",
        ConfirmCodes.ReasonRequired => "Soft checks did not pass: a reason is required. Nothing was sent.",
        _ => "The tap was refused (" + code + "). Nothing was sent.",
    };

    private Result<ConfirmResultView> Refuse(ConfirmTicketRow ticket, string code, string message)
    {
        var (t, i, a) = (ConfirmLog.Hash(ticket.Id), ConfirmLog.Hash(ticket.IntentPublicId), ConfirmLog.Hash(ticket.AccountId));
        ConfirmLog.Refused(logger, t, i, a, code);
        return Result.Failure<ConfirmResultView>(code == ConfirmCodes.NotFound ? Error.NotFound(code, message) : Error.Conflict(code, message));
    }
}

/// <summary>The owner's skip: a decision ("declined"), not an error. The reason is optional plain text of at most 200 characters.</summary>
public sealed record SkipTicketCommand(string Actor, Guid TicketId, string? Reason) : ICommand;

internal sealed class SkipTicketHandler(IConfirmTicketAccess access, ILogger<SkipTicketHandler> logger) : ICommandHandler<SkipTicketCommand, ConfirmResultView>
{
    public async ValueTask<Result<ConfirmResultView>> HandleAsync(SkipTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TicketOutcomes.IsOwnerActor(command.Actor))
        {
            return Result.Failure<ConfirmResultView>(Error.Forbidden(ConfirmCodes.OwnerOnly, "Only the signed-in owner can skip a ticket."));
        }

        var note = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (note is not null && !ConfirmPolicy.ReasonOk(note))
        {
            return Result.Failure<ConfirmResultView>(Error.Validation(ConfirmCodes.ValuesInvalid, "A skip reason is plain text of at most 200 characters."));
        }

        if (await access.FindAsync(command.TicketId, cancellationToken) is not { } ticket)
        {
            return Result.Failure<ConfirmResultView>(Error.NotFound(ConfirmCodes.NotFound, "There is no such ticket."));
        }

        if (ticket.Outcome is not null)
        {
            return ConfirmViews.Decided(ticket, TicketOutcomes.Skipped);
        }

        var written = await access.InsertOutcomeAsync(ticket.Id, TicketOutcomes.Skipped, TicketOutcomes.DeclinedReason, command.Actor, note, cancellationToken);
        if (written.Refused is { } code)
        {
            return Result.Failure<ConfirmResultView>(Error.Conflict(code, ConfirmTicketHandler.StateMessage(code)));
        }

        if (written.Duplicate || written.DecidedAt is not { } decidedAt)
        {
            return await access.FindAsync(ticket.Id, cancellationToken) is { } now
                ? ConfirmViews.Decided(now, TicketOutcomes.Skipped)
                : Result.Failure<ConfirmResultView>(Error.NotFound(ConfirmCodes.NotFound, "There is no such ticket."));
        }

        var (t, i, a) = (ConfirmLog.Hash(ticket.Id), ConfirmLog.Hash(ticket.IntentPublicId), ConfirmLog.Hash(ticket.AccountId));
        var millis = (long)(decidedAt - ticket.IssuedAt).TotalMilliseconds;
        ConfirmLog.Decided(logger, t, i, a, TicketOutcomes.Skipped, TicketOutcomes.DeclinedReason, millis);
        return Result.Success(new ConfirmResultView(ticket.Id, TicketOutcomes.Skipped, false, decidedAt));
    }
}

/// <summary>The unlocks of a session (default today in India). Read only.</summary>
public sealed record GetDayUnlocksQuery(DateOnly? Date) : IQuery<IReadOnlyList<DayUnlockView>>;

internal sealed class GetDayUnlocksHandler(IDayUnlockAccess access) : IQueryHandler<GetDayUnlocksQuery, IReadOnlyList<DayUnlockView>>
{
    public async ValueTask<IReadOnlyList<DayUnlockView>> HandleAsync(GetDayUnlocksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await access.ListAsync(query.Date ?? IndiaTime.Today(await access.NowAsync(cancellationToken)), cancellationToken);
    }
}

/// <summary>The owner's daily unlock of one account (spec P12-08 criterion 8, D1). The endpoint checked a fresh password for this act (the step-up window does not count); the actor comes from the session.</summary>
public sealed record UnlockDayCommand(string Actor, Guid? AccountId) : ICommand;

/// <summary>Only on a trading day (by the database's date in India) and only for an account in <c>semi_auto</c> or <c>auto_live</c>. The database stamps the time and the 15:30 expiry and refuses another day or a late unlock.</summary>
internal sealed class UnlockDayHandler(IDayUnlockAccess access, ITradingCalendar calendar, ILogger<UnlockDayHandler> logger) : ICommandHandler<UnlockDayCommand, DayUnlockView>
{
    public async ValueTask<Result<DayUnlockView>> HandleAsync(UnlockDayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TicketOutcomes.IsOwnerActor(command.Actor))
        {
            return Result.Failure<DayUnlockView>(Error.Forbidden(ConfirmCodes.OwnerOnly, "Only the signed-in owner can unlock the day."));
        }

        if (command.AccountId is not { } account)
        {
            return Result.Failure<DayUnlockView>(Error.Validation(ConfirmCodes.ValuesInvalid, "An unlock names an account."));
        }

        if (await access.EffectiveModeAsync(account, cancellationToken) is not { } mode)
        {
            return Result.Failure<DayUnlockView>(Error.NotFound(ConfirmCodes.NotFound, "There is no such account."));
        }

        if (!ConfirmPolicy.ConfirmableModes.Contains(mode))
        {
            return Refused(ConfirmCodes.ModeNotExecuting, "Only an account in semi_auto or auto_live is unlocked; this one is " + mode + ".");
        }

        var today = IndiaTime.Today(await access.NowAsync(cancellationToken));
        if (!(await calendar.GetAsync(cancellationToken)).IsTradingDay(today))
        {
            return Refused(ConfirmCodes.NotTradingDay, "Today is not a trading day; there is nothing to unlock.");
        }

        var (row, refused) = await access.InsertAsync(account, today, command.Actor, cancellationToken);
        if (row is null)
        {
            var code = refused ?? ConfirmCodes.UnlockClosed;
            return Refused(code, code == ConfirmCodes.UnlockClosed ? "The session has closed (15:30 India time) or the date is not today." : "The unlock was refused (" + code + ").");
        }

        var who = ConfirmLog.Hash(account);
        ConfirmLog.Unlocked(logger, who);
        return Result.Success(row);
    }

    private Result<DayUnlockView> Refused(string code, string message)
    {
        ConfirmLog.UnlockRefused(logger, code);
        return Result.Failure<DayUnlockView>(Error.Conflict(code, message));
    }
}
