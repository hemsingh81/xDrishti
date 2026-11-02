namespace XDrishti.Domain.Selection;

/// <summary>What the auto rule asks for (P15-06).</summary>
public enum EventFilterAutoAction
{
    /// <summary>No change: counted <c>auto_not_applied</c> with the reason of the decision.</summary>
    None,

    /// <summary>Turn the filter ON, recorded as an automatic decision.</summary>
    TurnOn,

    /// <summary>Turn an automatically enabled filter OFF, recorded as an automatic decision.</summary>
    TurnOff,
}
