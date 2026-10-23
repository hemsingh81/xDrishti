namespace XDrishti.Domain.Learning;

/// <summary>One purged walk-forward fold for one cell (P6-14): the window learnt on the train rows and how it did on the test rows.</summary>
/// <param name="Index">1-based fold number of the shared folds (P6-05).</param>
/// <param name="TestStart">First day of the test window.</param>
/// <param name="TestEnd">Last day of the test window.</param>
/// <param name="TrainCount">Cell trades kept on the train side after the embargo and the purge.</param>
/// <param name="WindowStartMinute">Start of the window learnt on the train rows; null when none was found.</param>
/// <param name="WindowEndMinute">End (exclusive) of that window.</param>
/// <param name="InTrades">Test trades inside the window.</param>
/// <param name="InRate">Their win rate.</param>
/// <param name="OutTrades">Test trades outside it.</param>
/// <param name="OutRate">Their win rate.</param>
/// <param name="Held">The window beat the outside in this fold; null when the fold could not judge it.</param>
/// <param name="SkipReason">Why the fold could not judge it.</param>
public sealed record EntryWindowFold(
    int Index,
    DateOnly TestStart,
    DateOnly TestEnd,
    int TrainCount,
    int? WindowStartMinute,
    int? WindowEndMinute,
    int InTrades,
    double? InRate,
    int OutTrades,
    double? OutRate,
    bool? Held,
    string? SkipReason);
