using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>An intent as the ticket issuer reads it: its levels, its state and the plan ticket behind it.</summary>
public sealed record TicketIntent(
    long IntentId, Guid PublicId, Guid AccountId, long PlanItemId, DateOnly Session, string Symbol, ExecSide Side, int Quantity, decimal Entry, decimal Stop, decimal Target, ExecState State);

/// <summary>The newest full P12-03 run of an intent: how many Hard checks did not pass and the sorted Soft codes that did not.</summary>
public sealed record TicketRun(long Id, int HardNonPass, IReadOnlyList<string> SoftNonPass);

/// <summary>What the card names besides the intent: the broker and account name, when the owner approved the plan ticket, the account's fixed capital (null when it is not fixed or not known) and the newest full run (null when none).</summary>
public sealed record TicketContext(string Broker, string AccountName, DateTimeOffset? ApprovedAt, decimal? Capital, TicketRun? Run);

/// <summary>A stored ticket with its outcome, if it has one.</summary>
public sealed record StoredTicket(
    Guid Id, Guid IntentPublicId, Guid AccountId, int Attempt, string Text, string TextHash, bool RequiresPassword, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string? Outcome, string? OutcomeReason, DateTimeOffset? DecidedAt);

/// <summary>A ticket to store. <see cref="RefuseAs"/> set (the fourth open ticket of an account) stores it closed <c>refused</c> with that reason in the same transaction: it never reaches the owner.</summary>
public sealed record TicketDraft(
    TicketIntent Intent,
    int Attempt,
    ConfirmTicketParams Params,
    string Text,
    string TextHash,
    string ClientOrderId,
    decimal RefPrice,
    PasswordRule Password,
    IReadOnlyList<string> SoftCodes,
    long? CheckRunId,
    string? RefuseAs);

/// <summary>The result of an insert: the stored ticket, or the database's refusal (null ticket and a reason), or neither when another writer stored this attempt first.</summary>
public sealed record TicketInsert(StoredTicket? Ticket, string? Refused);

/// <summary>A ticket the system closed (expired or refused) and the milliseconds from its issue to the decision, for the log.</summary>
public sealed record ClosedTicket(Guid TicketId, Guid IntentPublicId, Guid AccountId, string Outcome, string Reason, long MillisFromIssue);

/// <summary>
/// The confirm record as the execution service writes it (role xd_exec, P12-08b): issue tickets and close them <c>expired</c> or <c>refused</c>. The owner's two outcomes are written by the API only (the database refuses them from this role).
/// Nothing is updated or deleted.
/// </summary>
public interface IConfirmTicketStore
{
    Task<TicketIntent?> IntentAsync(long intentId, CancellationToken cancellationToken);

    /// <summary>Null when it cannot be read.</summary>
    Task<TicketContext?> ContextAsync(TicketIntent intent, CancellationToken cancellationToken);

    /// <summary>The tickets of an intent, attempt 1 first.</summary>
    Task<IReadOnlyList<StoredTicket>> TicketsAsync(Guid intentPublicId, CancellationToken cancellationToken);

    /// <summary>The tickets of an account that have no outcome and have not reached their expiry.</summary>
    Task<int> OpenCountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<TicketInsert> InsertAsync(TicketDraft draft, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Closes every ticket past its expiry without an outcome as <c>expired</c> (the database clock decides); the primary key decides a race with the owner's tap.</summary>
    Task<IReadOnlyList<ClosedTicket>> ExpireDueAsync(string actor, CancellationToken cancellationToken);

    /// <summary>Closes one ticket <c>refused</c>: true when written, false when it already had an outcome, null when there is no such ticket.</summary>
    Task<bool?> RefuseAsync(Guid ticketId, string reason, string actor, CancellationToken cancellationToken);
}

/// <summary>The newest ticket of an intent with its outcome and the database clock at the read.</summary>
public sealed record ConfirmDecision(Guid TicketId, string? Outcome, DateTimeOffset? DecidedAt, DateTimeOffset DbNow);

/// <summary>Reads the decision on the newest ticket of an intent (null when it has none). Throws when it cannot be read; the gate then stays closed.</summary>
public interface IConfirmDecisions
{
    Task<ConfirmDecision?> NewestAsync(long intentId, CancellationToken cancellationToken);
}

/// <summary>The confirm settings in force, from the newest good settings read; null when none was read recently (nothing is issued).</summary>
public interface IConfirmSettings
{
    ConfirmSettings? Current();
}

/// <summary>
/// The hand-off gate of a <c>semi_auto</c> entry (spec P12-08 criterion 12): open only when the newest ticket of the intent has a <c>confirmed</c> outcome at most 30 seconds old by the database clock. The default answers closed:
/// no answer is never a pass.
/// </summary>
public interface IConfirmGate
{
    Task<GateDecision> CheckAsync(long intentId, CancellationToken cancellationToken);
}

internal sealed class ClosedConfirmGate : IConfirmGate
{
    public Task<GateDecision> CheckAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(new GateDecision(false, "no_confirm_gate"));
}
