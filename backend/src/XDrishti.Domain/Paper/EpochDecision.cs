namespace XDrishti.Domain.Paper;

/// <summary>What a capture run found about the configuration.</summary>
public enum EpochOutcome
{
    /// <summary>Equal to the previous snapshot.</summary>
    Unchanged,

    /// <summary>Only risk-down or free differences: same epoch.</summary>
    Noted,

    /// <summary>A frozen difference: a new epoch starts.</summary>
    NewEpoch,

    /// <summary>No earlier snapshot to compare with: the snapshot is taken and no change is claimed.</summary>
    FirstSnapshot,

    /// <summary>A component could not be read: no change is claimed.</summary>
    Blind,
}

/// <summary>The decision of a capture run (spec P10-05): the outcome and whether each session counts.</summary>
/// <param name="Outcome">What was found.</param>
/// <param name="MeasureStatus">For each session: measured or straddle.</param>
public sealed record EpochDecision(EpochOutcome Outcome, IReadOnlyDictionary<DateOnly, string> MeasureStatus)
{
    /// <summary>Counts.</summary>
    public const string Measured = "measured";

    /// <summary>Stored and shown, never counted.</summary>
    public const string Straddle = "straddle";

    /// <summary>The most sessions a blind read may sit after the last full one and still count.</summary>
    public const int MaxBlindSessions = 3;

    /// <summary>Decides. <paramref name="sessionsSinceFull"/> gives the trading sessions since the last full snapshot, or null when there is none.</summary>
    public static EpochDecision Decide(IReadOnlyList<DateOnly> sessions, bool hasPrevious, bool partial, IReadOnlyList<ConfigChange> changes, Func<DateOnly, int?> sessionsSinceFull)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(sessionsSinceFull);
        var distinct = sessions.Distinct().ToList();
        if (partial)
        {
            return new EpochDecision(EpochOutcome.Blind, distinct.ToDictionary(s => s, s => sessionsSinceFull(s) is { } n && n <= MaxBlindSessions ? Measured : Straddle));
        }

        if (!hasPrevious)
        {
            return new EpochDecision(EpochOutcome.FirstSnapshot, distinct.ToDictionary(s => s, _ => Measured));
        }

        if (changes.Any(c => c.Class == ConfigClass.Frozen))
        {
            return new EpochDecision(EpochOutcome.NewEpoch, distinct.ToDictionary(s => s, _ => Straddle));
        }

        return new EpochDecision(changes.Count == 0 ? EpochOutcome.Unchanged : EpochOutcome.Noted, distinct.ToDictionary(s => s, _ => Measured));
    }
}
