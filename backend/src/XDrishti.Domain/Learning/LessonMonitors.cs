namespace XDrishti.Domain.Learning;

/// <summary>The spelling of monitor kinds and the monitors that are not built (spec P6-16).</summary>
public static class LessonMonitors
{
    /// <summary>Monitors that cannot run yet, with what they wait for. Printed with every scan.</summary>
    public static IReadOnlyList<string> NotBuilt { get; } =
    [
        "execution_gap: needs real fills (P10, P11)",
        "decision_drag: needs recorded decisions (P7, P10)",
        "data_quality: not a learning input yet",
        "drawdown_beyond_band and tail_losses: not built in this slice",
    ];

    /// <summary>The stored code of a monitor kind.</summary>
    public static string Code(this LessonMonitorKind kind) => kind switch
    {
        LessonMonitorKind.GrowingTag => "growing_tag",
        LessonMonitorKind.RollingBand => "rolling_band",
        LessonMonitorKind.Cusum => "cusum",
        LessonMonitorKind.Calibration => "calibration",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Reads a stored code back; null when unknown.</summary>
    public static LessonMonitorKind? TryParse(string? code) =>
        code is not null && Enum.GetValues<LessonMonitorKind>().Cast<LessonMonitorKind?>().FirstOrDefault(k => k!.Value.Code() == code) is { } found ? found : null;
}
