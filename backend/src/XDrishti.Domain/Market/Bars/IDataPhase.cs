namespace XDrishti.Domain.Market.Bars;

/// <summary>What the three phase records of a data job have in common: where the phase stands and how far it is (<c>Done</c> of <c>Total</c> files, instruments…).</summary>
public interface IDataPhase
{
    /// <summary>One of <see cref="PhaseStatus"/>.</summary>
    string Status { get; }

    DateTimeOffset? StartedAt { get; }

    DateTimeOffset? FinishedAt { get; }

    long Done { get; }

    long Total { get; }

    /// <summary>Why the phase was skipped or failed, in words for the owner.</summary>
    string? Message { get; }

    /// <summary>The same reason as a stable code (<see cref="DataErrors"/>).</summary>
    string? Code { get; }
}
