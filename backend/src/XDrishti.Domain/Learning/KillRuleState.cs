namespace XDrishti.Domain.Learning;

/// <summary>How a kill rule reads (spec CF-12, D2). Only <see cref="Fires"/> says anything against the strategy; <see cref="NotEvaluated"/> (a missing or short input) is never read as a pass or as a fail.</summary>
public enum KillRuleState
{
    /// <summary>The input is missing, short or not a number. The unset state.</summary>
    NotEvaluated = 0,

    /// <summary>The rule was evaluated and does not fire.</summary>
    Holds = 1,

    /// <summary>The rule was evaluated and fires (a proposal for the owner; this version acts on nothing).</summary>
    Fires = 2,

    /// <summary>The rule fires on the numbers but fewer than the required new trades have come since it last fired, so it is not counted again.</summary>
    Held = 3,
}
