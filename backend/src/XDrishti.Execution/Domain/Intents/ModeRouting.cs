namespace XDrishti.Execution.Domain.Intents;

/// <summary>
/// The routing table of spec P12-07 criterion 12 and P12-08 criterion 13, total over every mode and action class. <c>paper</c> and <c>manual_live</c> route nothing; <c>assisted</c> prepares a sheet for an entry only; <c>semi_auto</c> waits for the
/// owner's tap on an entry and acts on its own for the three risk-reducing classes; <c>auto_live</c> acts on its own for an entry and the reducing classes. <see cref="ActionClass.StopLoosen"/>, <see cref="ActionClass.AddToPosition"/> and
/// <see cref="ActionClass.ReEntry"/> are refused in every mode until P12-13 specifies them; <see cref="ActionClass.OutsidePlan"/> is refused in every mode. An unknown mode or no answer routes nothing; an unknown class is refused. A table, not a
/// permission: the account gate, the halts and the checks still decide whether anything may leave, and no mode can be stored as <c>semi_auto</c> or <c>auto_live</c> yet (ADR 0163).
/// </summary>
public static class ModeRouting
{
    public const string NotSupported = "execution.not_supported";
    public const string OutsidePlanCode = "execution.outside_plan";

    public static RouteKind Route(ExecMode? mode, ActionClass action) => action switch
    {
        ActionClass.Entry => mode switch
        {
            ExecMode.Assisted => RouteKind.PrepareSheet,
            ExecMode.SemiAuto => RouteKind.AwaitConfirm,
            ExecMode.AutoLive => RouteKind.Automatic,
            _ => RouteKind.None,
        },
        ActionClass.StopTighten or ActionClass.ExitReduce or ActionClass.CancelPending => mode is ExecMode.SemiAuto or ExecMode.AutoLive ? RouteKind.Automatic : RouteKind.None,
        _ => RouteKind.Refused,
    };

    /// <summary>The route with its refusal code. Entries are BUY only until P12-13: a SELL entry is refused in every mode.</summary>
    public static RouteDecision Decide(ExecMode? mode, ActionClass action, ExecSide side)
    {
        if (action == ActionClass.Entry && side != ExecSide.Buy)
        {
            return new(RouteKind.Refused, NotSupported);
        }

        var kind = Route(mode, action);
        return kind != RouteKind.Refused
            ? new(kind, null)
            : new(kind, action is ActionClass.StopLoosen or ActionClass.AddToPosition or ActionClass.ReEntry ? NotSupported : OutsidePlanCode);
    }
}
