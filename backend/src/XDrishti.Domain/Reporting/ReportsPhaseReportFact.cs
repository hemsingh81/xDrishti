namespace XDrishti.Domain.Reporting;

/// <summary>What the check found in one built report: whether it could be read and whether its evidence block was complete.</summary>
/// <param name="Name">The report.</param>
/// <param name="Readable">The handler answered; a refusal or failure is not readable and never counts as evidence.</param>
/// <param name="EvidenceComplete">The block holds the basis, the n, run keys and limits.</param>
/// <param name="Problem">What is missing or why it could not be read; null when fine.</param>
public sealed record ReportsPhaseReportFact(string Name, bool Readable, bool EvidenceComplete, string? Problem);
