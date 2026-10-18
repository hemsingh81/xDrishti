namespace XDrishti.Strategies.Model;

/// <summary>
/// The one place for windows (doc 07 §1, replacing <c>valid_window</c>): the earliest and the latest close time of a bar that may enter (both inclusive), the square-off time of an intraday strategy and the longest hold of a
/// swing strategy in sessions. Times are India clock times on the minute, 09:15 to 15:30. Self-contained: nothing is read from configuration.
/// </summary>
public sealed record TimeRules(TimeOnly? FirstEntry, TimeOnly? LastEntry, TimeOnly? SquareOff, int? MaxHoldSessions);
