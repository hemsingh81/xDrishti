namespace XDrishti.Domain.Reporting;

/// <summary>What a group is shrunk toward: the parent group's win rate, mean win and mean non-win (R of 0 or less). The mean figures are null when the parent has none.</summary>
internal sealed record GroupPriors(double WinRate, double? AvgWin, double? AvgNonWin);
