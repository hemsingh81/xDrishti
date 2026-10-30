namespace XDrishti.Execution.Domain.Intents;

/// <summary>
/// What an action does to the risk of an account (spec P12-07 criterion 12, meanings in P12-08 Contract). <see cref="Entry"/>, <see cref="StopLoosen"/>, <see cref="AddToPosition"/> and <see cref="ReEntry"/> add risk;
/// <see cref="StopTighten"/>, <see cref="ExitReduce"/> and <see cref="CancelPending"/> reduce it; <see cref="OutsidePlan"/> has no approved ticket behind it.
/// </summary>
public enum ActionClass
{
    Entry,
    StopTighten,
    StopLoosen,
    ExitReduce,
    CancelPending,
    AddToPosition,
    ReEntry,
    OutsidePlan,
}

/// <summary>What the system does with an action in a mode: nothing, prepare a sheet for the owner to type, wait for the owner's tap, act on its own, or refuse.</summary>
public enum RouteKind
{
    None,
    PrepareSheet,
    AwaitConfirm,
    Automatic,
    Refused,
}

/// <summary>The route of one action; <see cref="Code"/> names the refusal (<c>execution.not_supported</c> or <c>execution.outside_plan</c>) and is null otherwise.</summary>
public sealed record RouteDecision(RouteKind Kind, string? Code);
