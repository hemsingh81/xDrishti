using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Applies a rollback proposal: the champion becomes challenger (shadow) through the registry's designation and its changelog row (spec CF-10 D8, part 10d-1, ADR 0220). The actor is <c>owner:...</c> (the signed-in owner, from the session, no password: it only lowers reliance on ML)
/// or <c>auto</c> (only when <c>ml.auto_rollback</c> is ON). Applying twice is harmless. It never re-promotes the previous champion: that is the owner's act and a different command.
/// </summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="Actor">Who applies it.</param>
public sealed record ApplyRollbackCommand(long ProposalId, string Actor) : ICommand;

/// <summary>What applying did.</summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="ModelId">The champion it concerned.</param>
/// <param name="Trigger">Its trigger.</param>
/// <param name="State">The state of the proposal after this call (<c>applied</c>).</param>
/// <param name="AlreadyApplied">True when the proposal was applied before this call (nothing was changed now).</param>
/// <param name="Demoted">True when this call moved the model from champion to challenger; false when it was not the champion any more.</param>
/// <param name="PreviousChampionId">The champion this model replaced, or null.</param>
/// <param name="PreviousQualifies">Whether the previous champion's latest evaluation satisfied the predicate without the switch when the proposal was written. It is NOT re-promoted: with or without it there is no champion now, and only the owner can name one.</param>
/// <param name="Actor">Who applied it (the first time).</param>
public sealed record ApplyRollbackOutcome(long ProposalId, long ModelId, string Trigger, string State, bool AlreadyApplied, bool Demoted, long? PreviousChampionId, bool PreviousQualifies, string? Actor);
