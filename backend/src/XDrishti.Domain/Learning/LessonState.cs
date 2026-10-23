namespace XDrishti.Domain.Learning;

/// <summary>Where a lesson stands (spec P6-16, ADR 0078). Closed and Dismissed are final.</summary>
public enum LessonState
{
    /// <summary>A monitor found a problem; nothing has been done about it.</summary>
    Open,

    /// <summary>A fix (a hypothesis or an experiment) is linked; applying it is the owner's own act.</summary>
    Linked,

    /// <summary>Closed with the before and after numbers of the linked experiment; the result may be "no measurable change".</summary>
    Closed,

    /// <summary>Set aside by the owner without a result.</summary>
    Dismissed,
}

/// <summary>The spelling of a <see cref="LessonState"/> in the table and the API.</summary>
public static class LessonStateText
{
    /// <summary>The lower-case code.</summary>
    public static string Code(this LessonState state) => state.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static LessonState? TryParse(string? code) =>
        code is not null && Enum.GetValues<LessonState>().Cast<LessonState?>().FirstOrDefault(s => string.Equals(s!.Value.Code(), code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;
}
