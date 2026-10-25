namespace XDrishti.Domain.Reporting;

/// <summary>A part of the gap that is never given a number, with the reason (P8-04).</summary>
/// <param name="Step">exit_slippage or overrides.</param>
/// <param name="Reason">Why.</param>
public sealed record PlanVsActualNotDecomposed(string Step, string Reason);
