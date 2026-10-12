namespace XDrishti.Application.Abstractions.Market;

/// <summary>An instrument of the master list as tracking needs to see it.</summary>
public sealed record TrackableInstrument(Guid Id, string Symbol, bool Retired);
