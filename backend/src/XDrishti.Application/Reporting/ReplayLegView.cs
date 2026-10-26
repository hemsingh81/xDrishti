namespace XDrishti.Application.Reporting;

/// <summary>One fill of a trade with legs: <c>kind</c> is entry, add or exit; <c>share</c> the fraction of the maximum quantity; <c>reason</c> empty for an entry or an add.</summary>
public sealed record ReplayLegView(DateTimeOffset At, string Kind, double Price, double FilledPrice, decimal Share, string Reason);
