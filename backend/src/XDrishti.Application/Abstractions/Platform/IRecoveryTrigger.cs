namespace XDrishti.Application.Abstractions.Platform;

/// <summary>What asks for a recovery pass (ADR 0240, AU-02). Stored by code in <c>platform.recovery_passes.trigger</c>.</summary>
public enum RecoveryTrigger
{
    /// <summary>The application started.</summary>
    StartUp,

    /// <summary>A scheduled time was found late: the machine slept.</summary>
    Wake,

    /// <summary>A Data account connected successfully.</summary>
    BrokerReconnect,

    /// <summary>A step that waited for the market hours to end can go on.</summary>
    Resume,

    /// <summary>The owner asked for a pass on the Recovery screen (not debounced).</summary>
    Manual,
}

public static class RecoveryTriggerCodes
{
    public static string Code(RecoveryTrigger trigger) => trigger switch
    {
        RecoveryTrigger.StartUp => "startup",
        RecoveryTrigger.Wake => "wake",
        RecoveryTrigger.BrokerReconnect => "broker_reconnect",
        RecoveryTrigger.Resume => "resume",
        RecoveryTrigger.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}

/// <summary>Asks for a recovery pass. Never waits for it and never throws; a request inside the debounce, or while a pass runs, starts nothing.</summary>
public interface IRecoveryTrigger
{
    /// <summary>True when a pass was started by this request.</summary>
    bool Request(RecoveryTrigger trigger);
}
