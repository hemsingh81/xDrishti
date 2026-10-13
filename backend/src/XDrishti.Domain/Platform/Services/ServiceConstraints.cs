namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// What a service says about its own timing (design doc 04 §3.1); the rules (<see cref="ScheduleRules"/>) apply as soon as the service is registered. All times are India time.
/// </summary>
public sealed record ServiceConstraints
{
    /// <summary>The services this one should run after (their own time comes first, or this one follows them). Ids of services that are not registered are not checked.</summary>
    public IReadOnlyList<string> MustFollow { get; init; } = [];

    /// <summary>The service should not be up during this part of the day on trading days (a backup during market hours).</summary>
    public TimeRange? MustNotOverlap { get; init; }

    /// <summary>A window of this service should cover this part of the day (a live feed over the session).</summary>
    public TimeRange? MustCover { get; init; }

    /// <summary>A start before this time is warned about (end-of-day fetch before 15:35).</summary>
    public TimeOnly? StartNotBefore { get; init; }

    /// <summary>A start after this time is warned about (pre-open check after 09:15).</summary>
    public TimeOnly? StartNotAfter { get; init; }

    /// <summary>Why the start time matters; the text of the warnings about <see cref="StartNotBefore"/> and <see cref="StartNotAfter"/>.</summary>
    public string? StartAdvice { get; init; }

    /// <summary>A run on a trading day never starts before that day's session has ended (a special session that ends later moves it, ADR 0017 §2).</summary>
    public bool NotBeforeSessionEnd { get; init; }

    public CatchUpReach CatchUpReach { get; init; } = CatchUpReach.SameDay;
}
