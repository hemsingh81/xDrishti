namespace XDrishti.Application.Live.Push;

/// <summary>
/// The state of the live feed as the screen needs it: hub event <c>feedStatus</c> (pushed on connect, on a change of <c>state</c> or <c>subscribed</c>, and every 15 seconds) and the body of <c>GET /api/live/status</c> (the fallback when the hub is not connected).
/// <c>state</c> is the feed's own word: <c>connected</c> (streaming), <c>idle</c> (connected, nothing to follow), <c>connecting</c>, <c>reconnecting</c>, <c>closed</c> (outside its window), <c>paused</c> (the owner paused the service), <c>no_token</c>, <c>token_refused</c>,
/// <c>flapping</c>; or <c>not_running</c> when the feed reports nothing (never started, stopped, crashed: no heartbeat in the last 60 seconds). <c>subscribed</c> is the number of instruments the feed follows (0 unless it reports); <c>lastTickAt</c> the exchange
/// time of its latest tick of any instrument (UTC), null when none yet.
/// </summary>
public sealed record LiveFeedStatus(string State, int Subscribed, DateTimeOffset? LastTickAt);
