namespace XDrishti.Domain.Learning;

/// <summary>How a learning curve reads (spec CF-12, D5).</summary>
public enum CurveVerdict
{
    /// <summary>Fewer than three points, or points that cannot be read. The unset state.</summary>
    Insufficient = 0,

    /// <summary>Not shown to be rising or flat (always the case with three points: there is no standard error).</summary>
    Unclear = 1,

    /// <summary>The slope is above zero by 1.645 standard errors: more data should help this recipe.</summary>
    Rising = 2,

    /// <summary>Even the upper slope bound is below the minimum: more data will not help this recipe.</summary>
    Flat = 3,
}
