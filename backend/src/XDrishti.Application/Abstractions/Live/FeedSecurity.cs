namespace XDrishti.Application.Abstractions.Live;

/// <summary>A security on the feed: the exchange segment (<see cref="FeedSegments"/>) and Dhan's numeric security id. Security ids are unique within a segment only.</summary>
public readonly record struct FeedSecurity(string Segment, int SecurityId);
