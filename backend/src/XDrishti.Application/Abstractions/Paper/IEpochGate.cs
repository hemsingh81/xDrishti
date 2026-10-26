namespace XDrishti.Application.Abstractions.Paper;

/// <summary>The epoch a capture belongs to and whether each session counts as measured (spec P10-01).</summary>
/// <param name="Epoch">The epoch.</param>
/// <param name="MeasureStatus">For each asked session: <c>measured</c> or <c>straddle</c>.</param>
/// <param name="History">Every epoch, so a session of an earlier epoch that still has work is written under its own epoch; null means only <see cref="Epoch"/>.</param>
public sealed record EpochResolution(EpochRow Epoch, IReadOnlyDictionary<DateOnly, string> MeasureStatus, IReadOnlyList<EpochRow>? History = null);

/// <summary>
/// Decides the epoch of a capture. The default (<c>SingleEpochGate</c>) has one epoch, <c>first</c>, and every session is measured; P10-05 replaces it with the configuration governance.
/// </summary>
public interface IEpochGate
{
    /// <summary>The epoch in force, stored when there is none yet, and the measure status of each of <paramref name="sessions"/>.</summary>
    ValueTask<EpochResolution> ResolveAsync(IReadOnlyList<DateOnly> sessions, CancellationToken cancellationToken);
}
