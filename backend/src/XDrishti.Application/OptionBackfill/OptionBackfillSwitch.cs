using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionBackfill;

/// <summary>Reads the owner's backfill switch, its history, the probe acceptance, the progress and why the backfill is not running.</summary>
public sealed record GetOptionBackfillQuery : IQuery<OptionBackfillView>;

/// <summary>Switches the backfill as the owner (P14-03): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited.</summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Enabled">The new value of the switch.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ChangeOptionBackfillCommand(string Actor, bool Enabled, string Reason) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
public sealed record OptionBackfillSwitched(long Id, bool Enabled);

/// <summary>Records that the owner read the probe result and accepted it (P14-03 criterion 9): the backfill refuses to run without it. Same rules as the switch: owner actor from the session, fresh password in the endpoint, a note of 1 to 500 characters.</summary>
public sealed record AcceptBackfillProbeCommand(string Actor, string Note) : ICommand;

/// <summary>The answer of an acceptance: the row.</summary>
public sealed record BackfillProbeAccepted(long Id);

/// <summary>One change of the switch.</summary>
public sealed record OptionBackfillSwitchChangeView(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The acceptance of the probe result.</summary>
public sealed record BackfillProbeAcceptedView(long Id, string Actor, string Note, DateTimeOffset AcceptedAt);

/// <summary>How far the backfill is: the keys the plan has, finished, empty, answered "no data", given up and left, and the hours the left keys need at the current rate (null until the feed host reported progress). Counts only.</summary>
public sealed record OptionBackfillProgressView(int Planned, int Done, int Empty, int NoData, int GivenUp, int Left, double? EstimatedHours);

/// <summary>
/// The backfill as the owner sees it: the stored switch (<c>false</c> when never set), its history (newest first, the latest 50), the probe acceptance (null = not accepted), the state word of the heartbeat (null when the feed host never reported), the progress
/// and the reasons it is not running now. Nothing in it is a bar.
/// </summary>
public sealed record OptionBackfillView(bool Enabled, long? SwitchId, BackfillProbeAcceptedView? ProbeAccepted, string? State, DateTimeOffset? LastSeenAt, OptionBackfillProgressView? Progress, IReadOnlyList<string> NotRunningBecause, string Notice, IReadOnlyList<OptionBackfillSwitchChangeView> History);
