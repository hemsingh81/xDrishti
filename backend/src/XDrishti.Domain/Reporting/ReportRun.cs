namespace XDrishti.Domain.Reporting;

/// <summary>
/// A stored simulation run as a report sees it (ADR 0045, ADR 0099). Style and exit are labels of the run, not columns of a trade: null means "not recorded" and a report grouped by that dimension says so instead of guessing. The identity that the population rule
/// refuses to add twice is strategy + universe + window + style + exit.
/// </summary>
/// <param name="RunKey">The stored run key.</param>
/// <param name="Universe">What the run covered (a basket or instrument-list key); two runs of one universe and window are the same population.</param>
/// <param name="WindowStart">First India date of the run window.</param>
/// <param name="WindowEnd">Last India date of the run window.</param>
/// <param name="Style">The style label, <c>intraday</c> or <c>swing</c>; null when not recorded.</param>
/// <param name="Exit">The exit policy label; null when not recorded.</param>
/// <param name="Costed">The run's trades carry net R (a charge sheet was applied); false means gross only.</param>
/// <param name="Provisional">The run rests on something not yet confirmed (a default charge sheet, an engine not verified); every figure over it is marked provisional.</param>
public sealed record ReportRun(string RunKey, string Universe, DateOnly WindowStart, DateOnly WindowEnd, string? Style, string? Exit, bool Costed, bool Provisional = false);
