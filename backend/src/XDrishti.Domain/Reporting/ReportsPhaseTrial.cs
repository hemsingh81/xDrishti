namespace XDrishti.Domain.Reporting;

/// <summary>One of the owner's stopwatch trials.</summary>
/// <param name="Seconds">Seconds to the answer.</param>
/// <param name="Interactions">The owner's own count of clicks, key presses and menu opens.</param>
public sealed record ReportsPhaseTrial(double Seconds, int Interactions);
