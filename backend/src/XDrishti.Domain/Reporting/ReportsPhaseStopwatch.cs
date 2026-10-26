namespace XDrishti.Domain.Reporting;

/// <summary>The owner's recorded stopwatch note for the commit under test. Supplied, never verified (ADR 0080 pattern).</summary>
/// <param name="Trials">The trials.</param>
/// <param name="Date">When it was taken; null when not stated.</param>
public sealed record ReportsPhaseStopwatch(IReadOnlyList<ReportsPhaseTrial> Trials, DateOnly? Date);
