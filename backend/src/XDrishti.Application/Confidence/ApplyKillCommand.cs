using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Applies a kill proposal: every cell it names that is still Paper or Active moves to Paused through the lifecycle (spec CF-12 D2 and D3, part 12b, ADR 0219). The actor is <c>owner:...</c> (the signed-in owner, from the session, never from a body) or
/// <c>auto</c> (only when the setting is ON and the proposal is not proposal-only). Applying twice is harmless.
/// </summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="Actor">Who applies it.</param>
public sealed record ApplyKillCommand(long ProposalId, string Actor) : ICommand;

/// <summary>What applying did.</summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="Strategy">Its strategy.</param>
/// <param name="Rule">Its rule.</param>
/// <param name="State">The state of the proposal after this call (<c>applied</c>).</param>
/// <param name="AlreadyApplied">True when the proposal was applied before this call (nothing was changed now).</param>
/// <param name="PausedCells">The cells this call paused.</param>
/// <param name="SkippedCells">The cells that were no longer Paper or Active (already paused, retired or changed).</param>
/// <param name="Actor">Who applied it (the first time).</param>
public sealed record ApplyKillOutcome(long ProposalId, string Strategy, string Rule, string State, bool AlreadyApplied, IReadOnlyList<string> PausedCells, IReadOnlyList<string> SkippedCells, string? Actor);
