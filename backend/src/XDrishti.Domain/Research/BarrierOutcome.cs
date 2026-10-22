namespace XDrishti.Domain.Research;

/// <summary>Which barrier of the triple barrier ended a trade (P6-03, ADR 0065). Stored by name, never renamed.</summary>
public enum BarrierOutcome
{
    /// <summary>The upper barrier: the target level was touched. The only outcome with label 1.</summary>
    Target,

    /// <summary>The lower barrier: the stop level was touched.</summary>
    Stop,

    /// <summary>The session ended first: the square-off minute began.</summary>
    SessionEnd,

    /// <summary>The longest hold of a swing position ran out.</summary>
    MaxHold,

    /// <summary>An exit rule of the strategy closed the position before any barrier: the EMA trail or the time stop.</summary>
    RuleExit,
}
