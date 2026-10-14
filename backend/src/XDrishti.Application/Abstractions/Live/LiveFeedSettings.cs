namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The numbers of the feed (<c>Live:*</c>, bounds-checked by the host's options): the cap of the active set, how often ticks are flushed, how long silence is tolerated in market hours and the reconnect back-off. The host builds it
/// from its options; the feed logic only sees this record.
/// </summary>
public sealed record LiveFeedSettings(int MaxInstruments, TimeSpan FlushInterval, TimeSpan Silence, TimeSpan BackoffInitial, TimeSpan BackoffMax, TimeSpan SubscribeSpacing)
{
    /// <summary>The documented defaults: 200 instruments, flush every 2 s, 30 s of silence, back-off 1 s to 30 s, 250 ms between subscription messages.</summary>
    public static readonly LiveFeedSettings Default = new(200, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250));
}
