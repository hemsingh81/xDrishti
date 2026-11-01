namespace XDrishti.Application.Abstractions.Options;

/// <summary>The lot Dhan's file shows for an underlying on the day of a refresh (the lot of its nearest unexpired expiry).</summary>
public sealed record LotObservation(Guid UnderlyingId, int LotSize);
