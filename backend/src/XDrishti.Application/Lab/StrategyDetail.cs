namespace XDrishti.Application.Lab;

/// <summary>One family with the list of its versions (spec P5-01): no texts, those are read one version at a time.</summary>
public sealed record StrategyDetail(string Id, string Style, bool BuiltIn, DateTimeOffset CreatedAt, int LatestVersion, IReadOnlyList<VersionSummary> Versions);
