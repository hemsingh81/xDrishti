namespace XDrishti.Domain.Planning;

/// <summary>How a gate of the nightly pipeline read (spec P7-08). Only <see cref="Pass"/> lets the run go on; <see cref="Unavailable"/> (the input cannot be read) blocks exactly like <see cref="Fail"/>: the pipeline fails closed.</summary>
public enum GateStatus
{
    /// <summary>The input was read and is good.</summary>
    Pass,

    /// <summary>The input was read and is not good.</summary>
    Fail,

    /// <summary>The input could not be read or is not known; never a pass.</summary>
    Unavailable,
}
