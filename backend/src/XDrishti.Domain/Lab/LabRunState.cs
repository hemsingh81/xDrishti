namespace XDrishti.Domain.Lab;

/// <summary>The states of a Lab run (spec P5-02, D1; ADR 0058).</summary>
public enum LabRunState
{
    /// <summary>Stored, waiting for a slot.</summary>
    Queued,

    /// <summary>A job holds it.</summary>
    Running,

    /// <summary>Finished; the simulation run keys are in the record.</summary>
    Succeeded,

    /// <summary>Finished with located problems or an error; no result.</summary>
    Failed,

    /// <summary>Stopped at the owner's request; nothing was stored.</summary>
    Cancelled,

    /// <summary>The host stopped while it ran; never resumed, never shown as complete.</summary>
    Interrupted,
}
