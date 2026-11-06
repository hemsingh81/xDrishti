using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Confidence;

/// <summary>Evaluates one strategy's kill rules once for today (India date) and writes a proposal and a notice for every rule that fires (spec CF-12 D2, part 12b, ADR 0219). Idempotent per strategy and day.</summary>
/// <param name="Strategy">The strategy id (no `|`).</param>
public sealed record ProposeKillCommand(string? Strategy) : ICommand;

/// <summary>What one strategy's daily evaluation did.</summary>
/// <param name="Strategy">The strategy.</param>
/// <param name="AlreadyChecked">True when the strategy was already evaluated today (nothing was done).</param>
/// <param name="ProposalIds">The proposals written now.</param>
/// <param name="AppliedAutomatically">The proposals the automatic setting applied now (zero while the setting is OFF).</param>
/// <param name="FiringWithoutProposal">Rules that fire but made no proposal (an open proposal exists already, or no cell is Paper or Active).</param>
/// <param name="ApplyFailures">Automatic applies that failed or threw (the proposal stays open and is tried again by the next run).</param>
public sealed record ProposeKillOutcome(string Strategy, bool AlreadyChecked, IReadOnlyList<long> ProposalIds, int AppliedAutomatically, int FiringWithoutProposal, int ApplyFailures = 0);
