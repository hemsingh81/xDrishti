namespace XDrishti.Domain.Paper;

/// <summary>How a configuration difference affects the paper count (spec P10-05, ADR 0119).</summary>
public enum ConfigClass
{
    /// <summary>It changes what the system does, so the measured count restarts.</summary>
    Frozen,

    /// <summary>It only reduces risk (a limit lowered, a cell paused): the count goes on, the change is shown.</summary>
    RiskDown,

    /// <summary>Irrelevant to the numbers: stored and shown, never restarts the count.</summary>
    Free,
}

/// <summary>The stored text of a <see cref="ConfigClass"/>.</summary>
public static class ConfigClassText
{
    /// <summary>frozen, risk_down or free.</summary>
    public static string Code(this ConfigClass value) => value switch
    {
        ConfigClass.Frozen => "frozen",
        ConfigClass.RiskDown => "risk_down",
        ConfigClass.Free => "free",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
