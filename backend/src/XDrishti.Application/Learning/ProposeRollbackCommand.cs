using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Evaluates one champion's rollback triggers once for today (India date) and writes a proposal and a notice for every trigger that fires (spec CF-10 D8, part 10d-1, ADR 0220). Idempotent per model and day.</summary>
/// <param name="ModelId">The model; only the current champion is evaluated.</param>
public sealed record ProposeRollbackCommand(long ModelId) : ICommand;

/// <summary>What one model's daily evaluation did.</summary>
/// <param name="ModelId">The model.</param>
/// <param name="AlreadyChecked">True when the model was already evaluated today (nothing was evaluated again).</param>
/// <param name="NotChampion">True when the model is not the champion: there is nothing to roll back and nothing was recorded.</param>
/// <param name="ProposalIds">The proposals written now.</param>
/// <param name="AppliedAutomatically">The proposals the automatic setting applied now (zero while <c>ml.auto_rollback</c> is OFF).</param>
/// <param name="ApplyFailures">Automatic applies that failed or threw (the proposal stays open and is tried again by the next run).</param>
public sealed record ProposeRollbackOutcome(long ModelId, bool AlreadyChecked, bool NotChampion, IReadOnlyList<long> ProposalIds, int AppliedAutomatically, int ApplyFailures = 0);
