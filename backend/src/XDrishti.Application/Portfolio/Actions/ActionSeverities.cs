namespace XDrishti.Application.Portfolio.Actions;

/// <summary>How much an Action-center item matters: <c>high</c>, <c>medium</c>, <c>info</c>.</summary>
public static class ActionSeverities
{
    public const string High = "high";

    public const string Medium = "medium";

    public const string Info = "info";

    public static IReadOnlyList<string> All { get; } = [High, Medium, Info];

    /// <summary>0 for the most important; unknown values last.</summary>
    public static int Rank(string severity) => severity switch
    {
        High => 0,
        Medium => 1,
        Info => 2,
        _ => 3,
    };
}
