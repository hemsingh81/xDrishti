using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A stored plan with its current state and item counts.</summary>
/// <param name="Id">The plan id.</param>
/// <param name="Plan">The stored header and (for a single read) nothing else; items come from <see cref="IPlanStore.ItemsAsync"/>.</param>
/// <param name="State">The newest state row, Draft when none.</param>
/// <param name="Primary">Primary tickets.</param>
/// <param name="Reserve">Reserve tickets.</param>
/// <param name="Rejected">Rejected candidates.</param>
public sealed record PlanRow(long Id, NewPlan Plan, PlanState State, int Primary, int Reserve, int Rejected);

/// <summary>A stored item with its current state (Proposed when no row; a rejected candidate never has one).</summary>
/// <param name="Id">The item id.</param>
/// <param name="PlanId">The plan.</param>
/// <param name="Item">The stored candidate.</param>
/// <param name="State">The newest state row, Proposed when none.</param>
public sealed record PlanItemRow(long Id, long PlanId, NewPlanItem Item, TicketState State);

/// <summary>One state row of the trail, of the plan (<c>ItemId</c> null) or of an item.</summary>
/// <param name="Id">The row id; the trail is read in id order.</param>
/// <param name="ItemId">The item, or null for a plan row.</param>
/// <param name="From">The state before, as a code.</param>
/// <param name="To">The state after, as a code.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="ApprovalId">The approval row of an owner decision.</param>
/// <param name="At">When.</param>
public sealed record PlanEventRow(long Id, long? ItemId, string From, string To, string Actor, string Reason, long? ApprovalId, DateTimeOffset At);

/// <summary>The owner's decision stored beside a ticket move.</summary>
/// <param name="Decision">Approve, modify or reject.</param>
/// <param name="Modifications">What was edited; required for modify.</param>
/// <param name="ConfirmReason">The reason given to approve despite a failed check.</param>
public sealed record ApprovalInput(ApprovalDecision Decision, string? Modifications, string? ConfirmReason);

/// <summary>A ticket move to write; <c>Approval</c> is set exactly when the owner decides.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="From">The state the caller saw.</param>
/// <param name="To">The state to move to.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="Approval">The owner's decision: set exactly when <c>To</c> is approved, modified or rejected, and its decision must match <c>To</c>; the store throws otherwise.</param>
public sealed record ItemMove(long ItemId, TicketState From, TicketState To, string Actor, string Reason, ApprovalInput? Approval);

/// <summary>A plan move to write.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="From">The state the caller saw.</param>
/// <param name="To">The state to move to.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
public sealed record PlanMove(long PlanId, PlanState From, PlanState To, string Actor, string Reason);

/// <summary>What a move write did.</summary>
public enum MoveStatus
{
    /// <summary>The row was written.</summary>
    Created,

    /// <summary>The newest state was no longer <c>From</c>; nothing was written.</summary>
    Stale,

    /// <summary>The plan or item does not exist; nothing was written.</summary>
    NotFound,

    /// <summary>The tickets or the plan no longer support the move (computed in the store under the plan lock); nothing was written.</summary>
    Refused,

    /// <summary>An owner approval or modification at or after the plan's review cutoff; nothing was written.</summary>
    AfterCutoff,
}

/// <summary>What a publish did.</summary>
/// <param name="Id">The plan id (the existing one when the key was already stored).</param>
/// <param name="Created">False when the same key was already stored; the plan was not written again.</param>
/// <param name="RunRecorded">True when a published run row was written in the same transaction (only <see cref="IPlanStore.PublishWithRunAsync"/>).</param>
public sealed record PlanPublished(long Id, bool Created, bool RunRecorded = false);

/// <summary>The published run row stored in the same transaction as its plan (spec P7-08); the plan id and planned date come from the plan.</summary>
/// <param name="Manual">Whether a person started it.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
/// <param name="Reason">Why, in words (at most 500 characters).</param>
public sealed record PublishRun(bool Manual, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Reason);

/// <summary>One attempt of the nightly pipeline (spec P7-03; written by P7-08). Insert-only.</summary>
/// <param name="ForDate">The session planned for.</param>
/// <param name="PlanKey">The key, when known.</param>
/// <param name="Outcome">published, failed_closed or dry_run.</param>
/// <param name="FailedGate">The gate that failed; required for failed_closed.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="PlanId">The plan published; required for published.</param>
/// <param name="Manual">Whether a person started it (the phase check counts these).</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
public sealed record NewPlanRun(DateOnly ForDate, string? PlanKey, string Outcome, string? FailedGate, string Reason, long? PlanId, bool Manual, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);

/// <summary>One stored attempt of the nightly pipeline (spec P7-08).</summary>
/// <param name="Id">The row id.</param>
/// <param name="Attempt">1 for the first row of the planned session, then 2, 3 (counted by id).</param>
/// <param name="Run">What was stored.</param>
public sealed record PlanRunRow(long Id, int Attempt, NewPlanRun Run);
