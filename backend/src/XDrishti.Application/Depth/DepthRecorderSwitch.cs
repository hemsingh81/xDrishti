using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Depth;

/// <summary>Reads the owner's depth-recorder switch, its history and why the recorder is not running.</summary>
public sealed record GetDepthRecorderQuery : IQuery<DepthRecorderView>;

/// <summary>
/// Switches the depth recorder as the owner (P7-16): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Enabled">The new value of the switch.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ChangeDepthRecorderCommand(string Actor, bool Enabled, string Reason) : ICommand;

/// <summary>One change of the switch.</summary>
public sealed record DepthSwitchChangeView(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>
/// The recorder as the owner sees it: the stored switch (<c>false</c> when never set), its history (newest first, the latest 50), the state word of the recorder's heartbeat (null when the feed host never reported) and the reasons it is not running now
/// (empty when all three gates are open and it is in its window or between windows). Nothing in it is depth data.
/// </summary>
public sealed record DepthRecorderView(bool Enabled, long? SwitchId, string? State, DateTimeOffset? LastSeenAt, IReadOnlyList<string> NotRunningBecause, string Notice, IReadOnlyList<DepthSwitchChangeView> History);
