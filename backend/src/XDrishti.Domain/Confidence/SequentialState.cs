namespace XDrishti.Domain.Confidence;

/// <summary>
/// Where the sequential test stands (spec CF-09, D6). <see cref="Failure"/> is latched for the epoch; <see cref="Caution"/> and <see cref="OnTrack"/> describe the running sum now and can revert. None of them is a pass: the fixed gates of P10 decide that.
/// </summary>
public enum SequentialState
{
    /// <summary>The evidence favours neither side yet (also with no trade).</summary>
    Monitoring,

    /// <summary>The running sum sits at the on-track level: a label that can revert.</summary>
    OnTrack,

    /// <summary>The running sum is at or below the caution boundary now.</summary>
    Caution,

    /// <summary>The running sum reached the failure boundary at some point of the epoch (latched).</summary>
    Failure,
}
