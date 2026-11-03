namespace XDrishti.Application.OptionCosts;

/// <summary>What the build did for one underlying: <c>built</c>, <c>already_built</c> or <c>not_built</c>, the reason in words (<c>sessions 59 of 60</c>), the usable sessions found and the model id when there is one.</summary>
public sealed record SpreadModelBuildLine(string Symbol, string Status, string Reason, int UsableSessions, string? ModelId, int Buckets);
