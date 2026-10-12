namespace XDrishti.Application.Abstractions.Market;

/// <summary>A master-list row a pasted symbol may name; <c>Tracked</c> = tracked right now.</summary>
public sealed record ResolveCandidate(Guid Id, string Symbol, string Name, string Type, string? Isin, bool Retired, bool Tracked);
