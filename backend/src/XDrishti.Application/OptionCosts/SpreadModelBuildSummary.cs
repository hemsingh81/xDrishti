namespace XDrishti.Application.OptionCosts;

/// <summary>The result of a build: one line per underlying; no line when there is no underlying with chain sessions (then <see cref="Reason"/> says so).</summary>
public sealed record SpreadModelBuildSummary(IReadOnlyList<SpreadModelBuildLine> Lines, string? Reason);
