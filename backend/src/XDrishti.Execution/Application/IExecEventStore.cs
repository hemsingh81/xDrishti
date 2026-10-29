using XDrishti.Domain.Common;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>The stored projection of an intent: what <c>execution.intent_state</c> says. It must equal the fold of the events.</summary>
public sealed record ExecIntentState(ExecState State, int Seq, int FilledQty);

/// <summary>
/// The event log of intents. Insert-only: an event is added or refused, never changed. The Domain refuses first; the database refuses again by constraint and trigger (a second writer, a fork, an illegal pair), so the two cannot disagree silently.
/// </summary>
public interface IExecEventStore
{
    /// <summary>Stores a new intent in state planned. A second intent for the same account and ticket is a conflict.</summary>
    Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken);

    /// <summary>Adds the next event of an intent and moves its projection in the same transaction. A refused event stores nothing.</summary>
    Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken);

    /// <summary>The stored projection, or null for an unknown intent.</summary>
    Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken);

    /// <summary>Folds the stored events from the stored intent: the projection is this fold.</summary>
    Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken);
}
