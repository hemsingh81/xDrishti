using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A stored decision about an Action-center item: <see cref="State"/> is <see cref="ActionDecision.Snoozed"/> (until <see cref="SnoozedUntil"/>) or <see cref="ActionDecision.Done"/>.</summary>
public sealed record ActionStateRow(string Rule, string Key, ActionDecision State, DateOnly? SnoozedUntil, DateTimeOffset UpdatedAt);

/// <summary>The owner's decisions about Action-center items (ADR 0022): the only thing of the Action center that is stored. Nothing is ever deleted.</summary>
public interface IActionStates
{
    /// <summary>The decisions that still apply (snoozed or done; cleared ones are left out). Few rows: one read.</summary>
    ValueTask<IReadOnlyList<ActionStateRow>> ListLiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Collects a decision for the item (<see cref="IPortfolioUnitOfWork"/> saves it with its audit event). False, and nothing collected, when it is the decision already stored (or <see cref="ActionDecision.Cleared"/>
    /// for an item that has no live decision): repeating a decision is not a change.
    /// </summary>
    ValueTask<bool> SetAsync(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the given <c>done</c> decisions to <c>cleared</c> in one statement, but only the ones still as they were read (same state and time): a decision made again meanwhile is left alone. Executes at once (not part of a unit of work);
    /// returns the number of decisions cleared.
    /// </summary>
    ValueTask<int> ClearDoneAsync(IReadOnlyList<ActionStateRow> rows, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
