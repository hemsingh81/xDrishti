namespace XDrishti.Domain.Assistant;

/// <summary>An input a tool could not read, and why.</summary>
public sealed record ToolUnavailable(string Input, string Reason);

/// <summary>What an answer rests on (docs/specs/P9-00 envelope). <paramref name="SqlHash"/> is set by <c>run_sql</c> only: the SHA-256 of the whitespace-normalised query text.</summary>
public sealed record ToolEvidence(string Basis, IReadOnlyList<string> Population, string? DateRange, int? N, IReadOnlyList<string> Limits, IReadOnlyList<ToolUnavailable> Unavailable, string? SqlHash = null);
