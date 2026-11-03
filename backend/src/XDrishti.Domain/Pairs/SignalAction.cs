namespace XDrishti.Domain.Pairs;

/// <summary>What the state machine asks for on a bar.</summary>
public enum SignalAction
{
    Hold,
    EnterLongSpread,
    EnterShortSpread,
    Exit,
    Stop,
}
