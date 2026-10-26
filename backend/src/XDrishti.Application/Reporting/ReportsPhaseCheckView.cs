namespace XDrishti.Application.Reporting;

/// <summary>The P8 phase check as a read (spec P8-17).</summary>
/// <param name="Report">The criteria, the answer, the timings and the limits.</param>
/// <param name="Text">The printable report.</param>
public sealed record ReportsPhaseCheckView(ReportsPhaseReportView Report, string Text);
