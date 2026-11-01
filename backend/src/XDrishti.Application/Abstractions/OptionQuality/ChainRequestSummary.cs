namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>The requests of one underlying on one session: planned, ended ok, and the count by outcome word.</summary>
public sealed record ChainRequestSummary(Guid UnderlyingId, int Planned, int Ok, IReadOnlyDictionary<string, int> Outcomes);
