namespace XDrishti.Application.Abstractions.AutoSwitch;

/// <summary>The two states a change of the Auto switch can record.</summary>
public static class AutoSwitchKinds
{
    public const string On = "on";

    public const string Off = "off";
}

/// <summary>One change of the Auto switch (spec AU-17): who, when, which chain services it changed and the setting snapshot at that moment.</summary>
/// <param name="Id">The row.</param>
/// <param name="Kind"><see cref="AutoSwitchKinds"/>.</param>
/// <param name="Actor"><c>owner:&lt;e-mail&gt;</c> for the owner's act, <c>auto</c> for an automatic step; never an owner approval.</param>
/// <param name="At">When.</param>
/// <param name="Changed">The service ids this change resumed (ON) or paused (OFF); nothing else was touched.</param>
/// <param name="Snapshot">The setting snapshot: plain name and value pairs.</param>
public sealed record AutoSwitchChange(long Id, string Kind, string Actor, DateTimeOffset At, IReadOnlyList<string> Changed, IReadOnlyDictionary<string, string> Snapshot);

/// <summary><c>planning.auto_switch_changes</c> (spec AU-17): insert and select only.</summary>
public interface IAutoSwitchLog
{
    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<AutoSwitchChange>> RecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Appends a change and returns it.</summary>
    ValueTask<AutoSwitchChange> AppendAsync(string kind, string actor, DateTimeOffset at, IReadOnlyList<string> changed, IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken);
}
