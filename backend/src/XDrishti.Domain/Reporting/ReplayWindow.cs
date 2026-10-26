namespace XDrishti.Domain.Reporting;

/// <summary>The span of 1-minute bars a trade replay reads: epoch seconds of the first minute and of the last minute that can hold a bar (both included), and the India dates they fall on.</summary>
public sealed record ReplayWindow(long FromEpoch, long ToEpoch, DateOnly FromDate, DateOnly ToDate);
