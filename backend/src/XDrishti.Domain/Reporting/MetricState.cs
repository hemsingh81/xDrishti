namespace XDrishti.Domain.Reporting;

/// <summary>
/// The six honest states of a reported figure (P8-00 D3, ADR 0099). Only <see cref="Value"/> and <see cref="Provisional"/> carry a number; the other four never do, so a screen cannot draw a zero, a bar or a flat line for them.
/// </summary>
public enum MetricState
{
    /// <summary>A number computed from enough trades.</summary>
    Value,

    /// <summary>A number computed from enough trades, resting on an input that is marked provisional (the reason says which).</summary>
    Provisional,

    /// <summary>There are trades, but fewer than the minimum: no estimate is given, only the count.</summary>
    Insufficient,

    /// <summary>An input the figure needs does not exist (a label that was not recorded, a regime, an account).</summary>
    Unavailable,

    /// <summary>The rule needs more than exists even with enough trades (no losing trade for a profit factor).</summary>
    NotEvaluable,

    /// <summary>No trade at all.</summary>
    Empty,
}
