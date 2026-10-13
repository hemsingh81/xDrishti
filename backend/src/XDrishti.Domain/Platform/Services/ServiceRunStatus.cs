namespace XDrishti.Domain.Platform.Services;

/// <summary>How a run of a service stands. Stored by lower-case name.</summary>
public enum ServiceRunStatus
{
    Running,
    Succeeded,
    Failed,

    /// <summary>Not carried out, with the reason (the service was running already, the Data account cannot fetch).</summary>
    Skipped,
}
