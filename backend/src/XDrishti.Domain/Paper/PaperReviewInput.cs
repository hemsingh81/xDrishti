namespace XDrishti.Domain.Paper;

/// <summary>Everything the weekly paper review is made of (spec P10-03). A source that could not be read is null with its reason; its section says so and the rest is still produced.</summary>
/// <param name="Week">The ISO week.</param>
/// <param name="BuiltAt">When it is built.</param>
/// <param name="Epoch">The epoch, or null before the first capture.</param>
/// <param name="From">The first day of the window (the week cut at the epoch's start).</param>
/// <param name="To">The last day of the window (cut at the end of the epoch and at the last closed session).</param>
/// <param name="SessionsInWeek">Trading sessions of the week inside the epoch.</param>
/// <param name="SessionsCounted">Of those, the closed ones the review counts.</param>
/// <param name="Record">The paper facts, or null.</param>
/// <param name="RecordUnavailable">Why the paper facts are null.</param>
/// <param name="Config">The configuration facts, or null.</param>
/// <param name="ConfigUnavailable">Why the configuration facts are null.</param>
/// <param name="Comparison">The one line about the backtest comparison (already worded).</param>
public sealed record PaperReviewInput(
    PaperReviewWeek Week,
    DateTimeOffset BuiltAt,
    ReviewEpoch? Epoch,
    DateOnly From,
    DateOnly To,
    int SessionsInWeek,
    int SessionsCounted,
    PaperRecordFacts? Record,
    string? RecordUnavailable,
    ReviewConfig? Config,
    string? ConfigUnavailable,
    string Comparison);
