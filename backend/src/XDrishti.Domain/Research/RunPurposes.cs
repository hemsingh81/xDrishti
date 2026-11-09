namespace XDrishti.Domain.Research;

/// <summary>The stored text of a <see cref="RunPurpose"/>; the database check allows exactly these two.</summary>
public static class RunPurposes
{
    public const string ResearchText = "research";

    public const string TrainingText = "training";

    public static string Text(RunPurpose purpose) => purpose switch
    {
        RunPurpose.Research => ResearchText,
        RunPurpose.Training => TrainingText,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown run purpose."),
    };
}
