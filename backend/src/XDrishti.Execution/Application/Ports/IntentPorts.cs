using XDrishti.Domain.Planning;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Application.Ports;

/// <summary>
/// One allocation of an owner-approved plan ticket, as the intent creator reads it (read only, from <c>trading.*</c>, <c>workflow.approvals</c> and <c>market.*</c>): the ticket's current state, its numeric levels (P7-19; null when the
/// ticket has rules only), the broker security id (null when the instrument is unknown or ambiguous) and the approval behind it.
/// </summary>
public sealed record PlanEntryCandidate(
    long PlanId, long PlanItemId, long? ApprovalId, DateOnly Session, Guid AccountId, int Quantity, string Side, string Symbol, string? SecurityId, decimal? Entry, decimal? Stop, decimal? Target, TicketState ItemState);

/// <summary>The approved plan tickets with an allocation for the sessions from <c>session</c> to a week later. Null when the plan record cannot be read (the pass then does nothing).</summary>
public interface IIntentPlanSource
{
    Task<IReadOnlyList<PlanEntryCandidate>?> CandidatesAsync(DateOnly session, CancellationToken cancellationToken);
}

/// <summary>What the intent creator stores for a new intent. <see cref="CreatedBy"/> is the system actor.</summary>
public sealed record IntentDraft(
    Guid AccountId, long PlanId, long PlanItemId, long? ApprovalId, DateOnly Session, string Symbol, string SecurityId, int Quantity, decimal Entry, decimal Stop, decimal Target, ExecEnvironment Environment, string CreatedBy);

/// <summary>A stored intent with its projection: enough to decide its next move. <see cref="ModeTrailId"/> is the mode trail row in force when it was created, if any.</summary>
public sealed record IntentRow(long Id, Guid PublicId, Guid AccountId, long PlanItemId, DateOnly Session, string Symbol, ExecSide Side, decimal Entry, ExecState State, int Seq, long? ModeTrailId);

/// <summary>
/// The intents table as the intent creator uses it (role xd_exec): insert a new intent (one per account and plan ticket, the table's unique key decides) and list recent ones with their state. Moves go through
/// <see cref="IExecEventStore.AppendAsync"/>. Nothing is updated or deleted.
/// </summary>
public interface IIntentRecord
{
    /// <summary>The new intent in state planned; null when one already exists for the account and plan ticket, or the database refused the row.</summary>
    Task<IntentRow?> CreateAsync(IntentDraft draft, CancellationToken cancellationToken);

    /// <summary>The intents of the sessions from <paramref name="fromSession"/> on.</summary>
    Task<IReadOnlyList<IntentRow>> SinceAsync(DateOnly fromSession, CancellationToken cancellationToken);
}

/// <summary>The newest live price of a plan symbol; null when there is none or the symbol is ambiguous.</summary>
public interface IIntentQuotes
{
    Task<LivePrice?> LastAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>The settings the intent creator needs, from the newest good settings read; null when none was read recently (the pass then does nothing).</summary>
public interface IIntentSettings
{
    IntentSettings? Current();
}
