namespace XDrishti.Application.Abstractions.Replay;

/// <summary>The two automatic switches of the system replay (SR-03 D5). A null is <c>unavailable</c>: the table could not be read, and nothing acts.</summary>
public sealed record HoldOutAutoSwitches(bool? AutoRegister, bool? AutoRun);

/// <summary>The append-only settings switch table of the research schema (SR-03): the newest row of a name is in force; a name with no row reads OFF, and the migration seeds both ON (ADR 0282).</summary>
public interface IHoldOutAutoSettings
{
    /// <summary>The switches; both null when the table cannot be read.</summary>
    ValueTask<HoldOutAutoSwitches> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Appends a row for the setting <paramref name="name"/>.</summary>
    ValueTask AppendAsync(string name, bool enabled, string actor, string reason, DateTimeOffset now, CancellationToken cancellationToken);
}
