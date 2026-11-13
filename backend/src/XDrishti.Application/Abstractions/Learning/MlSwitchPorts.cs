namespace XDrishti.Application.Abstractions.Learning;

/// <summary>One of the three ML switches as stored: the newest row of its name, OFF with no id when none exists.</summary>
/// <param name="Id">The row in force, or null when none was ever stored.</param>
/// <param name="On">Whether the switch is ON.</param>
public sealed record MlSwitchState(long? Id, bool On);

/// <summary>The three ML switches as they stand (spec CF-10 D6 to D8): all default OFF.</summary>
/// <param name="Influence"><c>ml.influence.enabled</c>.</param>
/// <param name="AutoPromote"><c>ml.auto_promote</c>.</param>
/// <param name="AutoRollback"><c>ml.auto_rollback</c>.</param>
/// <param name="AutoHoldOutScore"><c>ml.holdout.auto_score</c> (CF-10e part 3): ON by the seed row of ADR 0282, OFF when no row exists.</param>
public sealed record MlSwitches(MlSwitchState Influence, MlSwitchState AutoPromote, MlSwitchState AutoRollback, MlSwitchState? AutoHoldOutScore = null)
{
    /// <summary>Every switch OFF, as when nothing is stored.</summary>
    public static MlSwitches AllOff { get; } = new(new MlSwitchState(null, false), new MlSwitchState(null, false), new MlSwitchState(null, false));
}

/// <summary>Reads the ML switches (<c>research.ml_switch_settings</c>). Read only.</summary>
public interface IMlSwitchSettings
{
    /// <summary>The switches now; each OFF with no id when no row of its name is stored.</summary>
    ValueTask<MlSwitches> CurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Appends a change of one ML switch (insert only). Only the owner's handler calls it.</summary>
public interface IMlSwitchStore
{
    /// <summary>Appends a row (the database stamps its id in commit order and its time) and returns the id.</summary>
    ValueTask<long> AppendAsync(string name, bool on, string actor, string reason, CancellationToken cancellationToken);
}
