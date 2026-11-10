namespace XDrishti.Domain.Reporting;

/// <summary>The relative strength return over one window of sessions.</summary>
/// <param name="Window">Sessions.</param>
/// <param name="Return">The return (fraction), or why there is none.</param>
public sealed record RelativeStrengthReturn(int Window, Metric Return);
