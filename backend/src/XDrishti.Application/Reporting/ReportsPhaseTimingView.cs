namespace XDrishti.Application.Reporting;

/// <summary>One timed read of the phase check (spec P8-17).</summary>
/// <param name="Operation">The operation id of the endpoint.</param>
/// <param name="AnswerPath">On the answer path of the exit question.</param>
/// <param name="ColdMs">The first call; null when the read was refused.</param>
/// <param name="WarmMs">The second call; null when the read was refused.</param>
public sealed record ReportsPhaseTimingView(string Operation, bool AnswerPath, long? ColdMs, long? WarmMs);
