namespace XDrishti.Application.Reporting;

/// <summary>One stored feature: its group (<c>bars</c>, <c>context</c>, <c>plan</c>), and a number only when <c>state</c> is value.</summary>
public sealed record ReplayFeatureItemView(string Name, string Source, string State, double? Value, string? Reason);
