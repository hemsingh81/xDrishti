namespace XDrishti.Domain.Readiness;

/// <summary>The state of one checklist row (spec CF-11 D1). <see cref="Unavailable"/> counts as a fail everywhere it is used: it is never a pass.</summary>
public enum RowStatus
{
    /// <summary>The input does not exist (no producer, no stored result, not computable). Not a pass.</summary>
    Unavailable = 0,

    /// <summary>The row's threshold is met.</summary>
    Pass = 1,

    /// <summary>The row's threshold is not met.</summary>
    Fail = 2,
}
