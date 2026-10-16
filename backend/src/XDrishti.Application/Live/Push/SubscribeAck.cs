namespace XDrishti.Application.Live.Push;

/// <summary>
/// The answer to <c>Subscribe</c>: how many instruments the connection now follows, whether the scope had more than the server follows per connection (1000 instruments, 1000 holding ids), in which case the rest get no pushes and the screen
/// keeps their prices from its reads, and <c>failed</c> when the server could not read the scope (the database is unavailable): the connection then follows nothing and the screen should subscribe again later. After a good answer the server sends the
/// current price of every followed instrument (one or more <c>pricesChanged</c> messages, within about a second), then only changes.
/// </summary>
public sealed record SubscribeAck(int Subscribed, bool Truncated, bool Failed);
