using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Capabilities;

/// <summary>Reads every capability with its state and the latest switches.</summary>
public sealed record GetCapabilitiesQuery : IQuery<CapabilitiesView>;

/// <summary>
/// The owner's switch of one capability (spec P8-18 Part A, ADR 0128). The actor must be <c>owner:...</c> and must come from the signed-in session (the endpoint asks for the password again to switch ON); it is never a field of a body. There is no other caller.
/// </summary>
/// <param name="Name">The capability.</param>
/// <param name="Enabled">True to switch on, false to switch off.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
public sealed record ChangeCapabilityCommand(string Name, bool Enabled, string Reason, string Actor) : ICommand;

/// <summary>What the switch did and what stands afterwards.</summary>
/// <param name="Changed">True when a row was written; false when the log already said the same.</param>
/// <param name="Capabilities">Every capability after the switch.</param>
public sealed record CapabilityChangeOutcome(bool Changed, CapabilitiesView Capabilities);

/// <summary>Every capability and the latest switches.</summary>
/// <param name="Items">The whitelist in screen order, each with its state (OFF when it has no row).</param>
/// <param name="History">The latest switches of all capabilities, newest first (at most 20).</param>
public sealed record CapabilitiesView(IReadOnlyList<CapabilityView> Items, IReadOnlyList<CapabilityHistoryView> History);

/// <summary>One capability.</summary>
/// <param name="Name">The stored name.</param>
/// <param name="Label">A few words.</param>
/// <param name="Meaning">What ON allows.</param>
/// <param name="Enabled">The newest row says on. False when there is no row.</param>
/// <param name="ChangedAt">When the newest row was written; null when there is none.</param>
/// <param name="Reason">The reason of the newest row; null when there is none.</param>
public sealed record CapabilityView(string Name, string Label, string Meaning, bool Enabled, DateTimeOffset? ChangedAt, string? Reason);

/// <summary>One switch in the history.</summary>
/// <param name="Id">The row.</param>
/// <param name="Name">The capability.</param>
/// <param name="Enabled">The state it set.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="At">When.</param>
public sealed record CapabilityHistoryView(long Id, string Name, bool Enabled, string Actor, string Reason, DateTimeOffset At);
