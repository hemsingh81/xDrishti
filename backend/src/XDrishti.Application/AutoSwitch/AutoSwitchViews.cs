using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.AutoSwitch;

/// <summary>The Auto switch with its chain, its preconditions and the last night (spec AU-17). Read only.</summary>
public sealed record GetAutoQuery : IQuery<AutoView>;

/// <summary>Turns Auto ON as the owner. <c>FreshPassword</c> is true only after the endpoint verified a fresh password for this act.</summary>
public sealed record TurnAutoOnCommand(string Actor, bool FreshPassword) : ICommand;

/// <summary>Turns Auto OFF as the owner (a session is enough: the safe direction).</summary>
public sealed record TurnAutoOffCommand(string Actor) : ICommand;

/// <summary>One thing Auto needs. <c>Status</c> is <c>pass</c>, <c>fail</c> or <c>unavailable</c>; only <c>pass</c> lets Auto turn ON.</summary>
public sealed record AutoPreconditionView(string Id, string Label, string Status, string Detail);

/// <summary>One service of the chain. <c>ResumedByAuto</c> is true when turning Auto ON resumed it (OFF pauses it again).</summary>
public sealed record AutoChainItemView(string ServiceId, string Name, bool Enabled, bool ResumedByAuto);

/// <summary>The newest nightly run: <c>no_run</c>, <c>published</c> or <c>failed_closed</c> (then no suggestions are shown and <c>Reason</c> says why).</summary>
public sealed record AutoNightView(string State, DateOnly? ForDate, string? FailedGate, string? Reason);

/// <summary>One recorded change.</summary>
public sealed record AutoChangeView(long Id, string Kind, string Actor, DateTimeOffset At, IReadOnlyList<string> Changed, IReadOnlyDictionary<string, string> Snapshot);

/// <summary>The whole readout. <c>Complete</c> is true only when Auto is ON and every chain service is on; with Auto ON and a service paused it is false and <c>Note</c> names the service.</summary>
public sealed record AutoView(
    bool On,
    bool Complete,
    string? Note,
    string Builder,
    IReadOnlyList<AutoChainItemView> Chain,
    IReadOnlyList<AutoPreconditionView> Preconditions,
    AutoNightView Night,
    IReadOnlyList<AutoChangeView> Changes,
    string NoOrderNotice);

internal static class AutoSwitchWording
{
    public const string NoOrderNotice = "Auto resumes the data and planning services and produces suggestions only. It never approves, arms or places an order, and it does not change autonomy.";
}

internal static class AutoSwitchErrors
{
    public static readonly Error OwnerOnly = Error.Forbidden("auto.owner_only", "Only the signed-in owner changes Auto.");

    public static readonly Error PasswordRequired = Error.Forbidden("auto.password_required", "Turning Auto ON needs your password for this change.");

    public static Error Refused(string items) => Error.Conflict("auto.preconditions_failed", "Auto cannot turn ON yet: " + items);

    public static Error Failed(string detail) => Error.Conflict("auto.chain_failed", "Auto was not turned ON, nothing was left changed: " + detail);
}
