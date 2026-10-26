namespace XDrishti.Application.Reporting;

/// <summary>One bar: <c>t</c> is the epoch seconds of its start.</summary>
public sealed record ReplayBarView(long T, double O, double H, double L, double C, long V);
