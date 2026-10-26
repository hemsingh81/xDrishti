namespace XDrishti.Domain.Paper;

/// <summary>An epoch as the review reads it (spec P10-03).</summary>
/// <param name="Id">The id.</param>
/// <param name="StartedOn">The first session.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="Reason">Why it started.</param>
public sealed record ReviewEpoch(long Id, DateOnly StartedOn, string Kind, string Reason);
