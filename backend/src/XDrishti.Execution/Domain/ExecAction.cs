namespace XDrishti.Execution.Domain;

/// <summary>What the engine may do to a position. A stop only tightens, a position is never added to, a ticket is never re-entered.</summary>
public enum ExecAction
{
    Entry,
    StopTighten,
    Exit,
    StopLoosen,
    AddToPosition,
    ReEntry,
}

public enum ActionVerdict
{
    Allowed,
    Refused,
}

/// <summary>Whether an action is allowed by policy at all, in any mode (the gate, checks and halts still apply). The three refused actions stay refused until a later slice decides otherwise.</summary>
public static class ActionPolicy
{
    public static ActionVerdict Check(ExecAction action, ExecMode mode) =>
        action is ExecAction.StopLoosen or ExecAction.AddToPosition or ExecAction.ReEntry ? ActionVerdict.Refused : ActionVerdict.Allowed;
}
