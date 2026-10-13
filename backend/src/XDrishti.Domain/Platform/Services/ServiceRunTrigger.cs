namespace XDrishti.Domain.Platform.Services;

/// <summary>What started a run of a service (ADR 0018 §5). Stored by code: scheduled · catch_up · after · manual.</summary>
public enum ServiceRunTrigger
{
    /// <summary>Its schedule.</summary>
    Scheduled,

    /// <summary>The application started after a scheduled time had passed (ADR 0018 §4).</summary>
    CatchUp,

    /// <summary>Another service finished successfully and this one is set to run after it.</summary>
    After,

    /// <summary>The owner pressed Run now.</summary>
    Manual,
}
