using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionChain;

/// <summary>Reads the owner's option-chain-recorder switch, its history and why the recorder is not running.</summary>
public sealed record GetOptionChainRecorderQuery : IQuery<OptionChainRecorderView>;

/// <summary>Switches the option-chain recorder as the owner (P14-02): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password (the endpoint), never from a body. Every call appends a row; none is edited.</summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Enabled">The new value of the switch.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ChangeOptionChainRecorderCommand(string Actor, bool Enabled, string Reason) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
public sealed record OptionChainRecorderSwitched(long Id, bool Enabled);

/// <summary>One change of the switch.</summary>
public sealed record OptionChainSwitchChangeView(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>
/// The recorder as the owner sees it: the stored switch (<c>false</c> when never set), its history (newest first, the latest 50), the state word of the recorder's heartbeat (null when the feed host never reported) and the reasons it is not running now.
/// Nothing in it is chain data.
/// </summary>
public sealed record OptionChainRecorderView(bool Enabled, long? SwitchId, string? State, DateTimeOffset? LastSeenAt, IReadOnlyList<string> NotRunningBecause, string Notice, IReadOnlyList<OptionChainSwitchChangeView> History);
