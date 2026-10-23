namespace XDrishti.Domain.Planning;

/// <summary>What became of a ticket when it was walked through the stored bars of its session (P7-13, ADR 0090).</summary>
public enum GradeStatus
{
    /// <summary>The ticket entered and left; it has an R.</summary>
    Graded,

    /// <summary>The replay took no trade on the planned session: the entry condition did not hold, or the order did not fill.</summary>
    NotTriggered,

    /// <summary>The ticket cannot be replayed (the reason is stored); it is never guessed.</summary>
    NotGradable,
}

/// <summary>The spelling of a <see cref="GradeStatus"/> in the table and the API.</summary>
public static class GradeStatusText
{
    /// <summary>The snake-case code.</summary>
    public static string Code(this GradeStatus status) => status switch
    {
        GradeStatus.Graded => "graded",
        GradeStatus.NotTriggered => "not_triggered",
        _ => "not_gradable",
    };

    /// <summary>Reads a code back; null when unknown.</summary>
    public static GradeStatus? TryParse(string? code) => code switch
    {
        "graded" => GradeStatus.Graded,
        "not_triggered" => GradeStatus.NotTriggered,
        "not_gradable" => GradeStatus.NotGradable,
        _ => null,
    };
}
