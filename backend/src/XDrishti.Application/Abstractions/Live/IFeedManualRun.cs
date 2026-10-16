namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The "Run now" marker of the live feed: the <c>platform.app_settings</c> row <see cref="Key"/> holds an ISO-8601 UTC instant; while it is in the future the feed is wanted up even outside its window (the run is recorded with
/// trigger <c>manual</c>). Written by the API's Run now (the Services screen), read by the feed - no command channel to a process (ADR 0024 §1).
/// </summary>
public interface IFeedManualRun
{
    /// <summary>The key of the settings row.</summary>
    public const string Key = "live-feed.manual_until";

    /// <summary>The instant until which a manual run is wanted; null when there is no marker or it cannot be read.</summary>
    ValueTask<DateTimeOffset?> GetManualUntilAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Collects the marker "wanted up until <paramref name="until"/>" in the scoped context; the use case saves it together with its audit event (<c>IPlatformUnitOfWork</c>). The feed sees it within its 15-second look.
    /// </summary>
    ValueTask SetManualUntilAsync(DateTimeOffset until, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Collects the end of the marker in the scoped context (it is set to <paramref name="now"/>, no longer in the future): a manual run in force ends within the feed's next look. Nothing is collected when there is no marker. Saved by the
    /// use case with its audit event - a pause, or a save with the service switched off, so that pausing really pauses.
    /// </summary>
    ValueTask ClearAsync(string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
