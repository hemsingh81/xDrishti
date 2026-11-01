namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>A finished fetch of one rolling series over a 30-day window (<paramref name="Outcome"/> is <c>ok</c>, <c>empty</c> or <c>no_data</c>) and the key the results are stored by.</summary>
public sealed record RollingWindow(Guid UnderlyingId, char ExpiryFlag, int ExpiryCode, int Offset, string Side, int IntervalMinutes, DateOnly From, DateOnly To, string Outcome, string FetchKey);
