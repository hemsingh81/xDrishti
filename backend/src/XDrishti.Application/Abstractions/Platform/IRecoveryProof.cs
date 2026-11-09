namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// The proof of a recovery step (<paramref name="AllRecovered"/> false means the item is <c>partial</c>): <paramref name="Detail"/> is the per-session state as JSON (stored in <c>recovery_items.detail</c>) and
/// <paramref name="Reason"/> names the sessions not recovered by date.
/// </summary>
public sealed record RecoveryProofResult(bool AllRecovered, string Detail, string? Reason);

/// <summary>
/// Checks after a successful recovery step that the sessions it covered really have their data (spec AU-03, ADR 0240): a service that can say what it should have produced declares it here and the runner asks.
/// Reads the existing gap, aggregate and quality stores only, a bounded read per instrument over the step's sessions (never the bar history); never writes.
/// </summary>
public interface IRecoveryProof
{
    /// <summary>The service whose steps this proves.</summary>
    string ServiceId { get; }

    /// <param name="sessions">The sessions the step covered, oldest first.</param>
    /// <param name="runStartedAt">When the run that made them up started: a quality run that finished before it proves nothing about them.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    ValueTask<RecoveryProofResult> ProveAsync(IReadOnlyList<DateOnly> sessions, DateTimeOffset runStartedAt, CancellationToken cancellationToken);
}
