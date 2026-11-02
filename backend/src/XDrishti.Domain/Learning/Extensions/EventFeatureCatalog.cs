namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extension <c>ev-1</c> (P15-05, ADR 0185): the event features, in the order they are stored.</summary>
public static class EventFeatureCatalog
{
    public const string Extension = "ev";

    public const string Version = "ev-1";

    public const string DaysToResults = "days_to_results";

    public const string DaysSinceResults = "days_since_results";

    public const string ResultsCar3 = "results_car3";

    public static readonly IReadOnlyList<string> Names = [DaysToResults, DaysSinceResults, ResultsCar3];

    public static ExtensionGroup Group { get; } = new(Extension, Version, Names);
}
