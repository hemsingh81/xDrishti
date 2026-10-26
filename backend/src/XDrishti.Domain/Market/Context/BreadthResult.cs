namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The breadth of one day (P3-01e): <paramref name="Value"/> is the percent (0 to 100) of the voters whose close is above their average, null when the state is <see cref="Insufficient"/>; <paramref name="Voters"/> voted,
/// <paramref name="Universe"/> shares were asked, <paramref name="Excluded"/> did not vote and <paramref name="ExcludedByReason"/> says why (only reasons that occurred).
/// </summary>
public sealed record BreadthResult(double? Value, int Voters, int Universe, int Excluded, IReadOnlyDictionary<string, int> ExcludedByReason, string State)
{
    /// <summary>A number is stored.</summary>
    public const string ValueState = "value";

    /// <summary>Too few voters for a number (a thin sample is not a number).</summary>
    public const string Insufficient = "insufficient";

    /// <summary>No bar of the share on the day (or not a complete one).</summary>
    public const string NoBar = "no_bar";

    /// <summary>The share lacks a complete bar on one of the window sessions.</summary>
    public const string Warmup = "warmup";

    /// <summary>A close of the window is not a positive number.</summary>
    public const string BadBar = "bad_bar";

    /// <summary>An ex-date of a corporate action (any status) falls in the window: the raw closes would jump.</summary>
    public const string ActionInWindow = "action_in_window";
}
