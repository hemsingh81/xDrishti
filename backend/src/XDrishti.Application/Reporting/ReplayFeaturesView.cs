namespace XDrishti.Application.Reporting;

/// <summary>The decision-time features stored for the trade (catalog <c>version</c>): <c>state</c> is value, or unavailable with the <c>reason</c> (no row stored, or a row of another decision). Never a zero for an unknown.</summary>
public sealed record ReplayFeaturesView(string State, string Version, string? Reason, IReadOnlyList<ReplayFeatureItemView> Items);
