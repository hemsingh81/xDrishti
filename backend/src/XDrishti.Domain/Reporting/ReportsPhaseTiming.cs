namespace XDrishti.Domain.Reporting;

/// <summary>One timed report read of the check. A null time means the read was refused or not made.</summary>
/// <param name="Operation">The operation id of the endpoint (<c>GetReportWhatWorks</c>).</param>
/// <param name="AnswerPath">The operation is on the answer path of the exit question.</param>
/// <param name="ColdMs">The first call of this check.</param>
/// <param name="WarmMs">The second call.</param>
public sealed record ReportsPhaseTiming(string Operation, bool AnswerPath, long? ColdMs, long? WarmMs);
