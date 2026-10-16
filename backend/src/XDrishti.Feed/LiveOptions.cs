using System.ComponentModel.DataAnnotations;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Feed;

/// <summary>
/// The numbers of the live feed (<c>Live:*</c>, ADR 0024, documented in doc 16), bounds-checked at start: the cap of the active set, how often ticks are flushed to the database, how long a silent connection is tolerated in market hours and
/// the reconnect back-off (and <c>Live:ManualRunMinutes</c>, read by the API for "Run now": <c>XDrishti.Infrastructure.Platform.LiveManualRunOptions</c>). The window itself (09:00-15:35) is not here: it is the schedule of the <c>live-feed</c> service, edited on the Services screen.
/// </summary>
public sealed class LiveOptions : IValidatableObject
{
    public const string SectionName = "Live";

    /// <summary>The most instruments subscribed at once (alerts first, then indices, then holdings by value). Dhan allows 5,000 on one connection.</summary>
    [Range(1, 5000)]
    public int MaxWsInstruments { get; set; } = 200;

    /// <summary>How often the latest price of each instrument is written (one statement per flush).</summary>
    [Range(1, 30)]
    public int FlushSeconds { get; set; } = 2;

    /// <summary>No packet for this long in market hours counts as a dead connection.</summary>
    [Range(5, 300)]
    public int SilenceSeconds { get; set; } = 30;

    /// <summary>The first reconnect wait; each following one doubles up to <see cref="BackoffMaxSeconds"/>.</summary>
    [Range(1, 60)]
    public int BackoffInitialSeconds { get; set; } = 1;

    [Range(1, 300)]
    public int BackoffMaxSeconds { get; set; } = 30;

    /// <summary>The pause between two subscription messages.</summary>
    [Range(0, 5000)]
    public int SubscribeSpacingMilliseconds { get; set; } = 250;

    /// <summary>The settings the feed logic works with.</summary>
    public LiveFeedSettings ToSettings() => new(
        MaxWsInstruments,
        TimeSpan.FromSeconds(FlushSeconds),
        TimeSpan.FromSeconds(SilenceSeconds),
        TimeSpan.FromSeconds(BackoffInitialSeconds),
        TimeSpan.FromSeconds(BackoffMaxSeconds),
        TimeSpan.FromMilliseconds(SubscribeSpacingMilliseconds));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BackoffMaxSeconds < BackoffInitialSeconds)
        {
            yield return new ValidationResult("Live:BackoffMaxSeconds must not be smaller than Live:BackoffInitialSeconds.", [nameof(BackoffMaxSeconds)]);
        }
    }
}
