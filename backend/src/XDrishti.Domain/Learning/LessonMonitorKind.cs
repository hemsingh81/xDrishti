namespace XDrishti.Domain.Learning;

/// <summary>The monitors that can open a lesson from replayed history (spec P6-16). Others are listed in <see cref="LessonMonitors.NotBuilt"/>.</summary>
public enum LessonMonitorKind
{
    /// <summary>A tag whose share of a cell's losses is growing (P6-15).</summary>
    GrowingTag,

    /// <summary>Rolling expectancy of a cell below the band of its reference (P6-09).</summary>
    RollingBand,

    /// <summary>A CUSUM alarm on a cell (P6-09).</summary>
    Cusum,

    /// <summary>The calibration of a registered model drifted (P6-09).</summary>
    Calibration,
}
