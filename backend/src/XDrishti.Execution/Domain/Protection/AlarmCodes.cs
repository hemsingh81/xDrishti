namespace XDrishti.Execution.Domain.Protection;

public enum AlarmSeverity
{
    Info,
    Warn,
    Critical,
}

/// <summary>The twelve alarm codes of spec P12-04 criterion 11 and their severities. A code outside this list cannot be raised.</summary>
public static class AlarmCodes
{
    public const string ProtectionPending = "protection_pending";
    public const string ProtectionBreach = "protection_breach";
    public const string StopReplaced = "stop_replaced";
    public const string StopMismatch = "stop_mismatch";
    public const string StopTriggeredUnfilled = "stop_triggered_unfilled";
    public const string FlattenStarted = "flatten_started";
    public const string FlattenIncomplete = "flatten_incomplete";
    public const string Oversold = "oversold";
    public const string SendUnknown = "send_unknown";
    public const string ProtectionUnknown = "protection_unknown";
    public const string ExitFailed = "exit_failed";
    public const string BrokerSquareoff = "broker_squareoff";

    public static readonly IReadOnlyDictionary<string, AlarmSeverity> All = new Dictionary<string, AlarmSeverity>(StringComparer.Ordinal)
    {
        [ProtectionPending] = AlarmSeverity.Warn,
        [ProtectionBreach] = AlarmSeverity.Critical,
        [StopReplaced] = AlarmSeverity.Info,
        [StopMismatch] = AlarmSeverity.Critical,
        [StopTriggeredUnfilled] = AlarmSeverity.Warn,
        [FlattenStarted] = AlarmSeverity.Critical,
        [FlattenIncomplete] = AlarmSeverity.Critical,
        [Oversold] = AlarmSeverity.Critical,
        [SendUnknown] = AlarmSeverity.Warn,
        [ProtectionUnknown] = AlarmSeverity.Critical,
        [ExitFailed] = AlarmSeverity.Critical,
        [BrokerSquareoff] = AlarmSeverity.Info,
    };
}
