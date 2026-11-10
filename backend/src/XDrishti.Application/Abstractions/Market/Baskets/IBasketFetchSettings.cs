using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary><c>Baskets:System:Fetch:*</c> (BK-01): safe defaults, fetching OFF. Off means zero HTTP calls.</summary>
public interface IBasketFetchSettings
{
    /// <summary><c>Baskets:System:Fetch:Enabled</c>, default false.</summary>
    bool Enabled { get; }

    /// <summary>Descriptive name plus owner contact; empty = the fetch refuses to run.</summary>
    string UserAgent { get; }

    FetchLimits Limits { get; }

    /// <summary>"A" (nsearchives) or "B" (niftyindices): the host asked first when a source has both (default A).</summary>
    string PreferredHost { get; }

    /// <summary>The India time of day from which a source is asked for the day (default 07:30).</summary>
    TimeOnly RunAt { get; }

    /// <summary>Minutes to wait before each retry of a failed attempt (default 5, 30, 120: four tries in all).</summary>
    IReadOnlyList<int> BackoffMinutes { get; }

    /// <summary>Days without a successful check after which a source is <c>stale</c> (default 3).</summary>
    int StaleAfterDays { get; }

    /// <summary>Hours a source rests after 403 or 429 (default 24).</summary>
    int BackoffHours { get; }

    IReadOnlyList<int> ReviewMonths { get; }

    int ReviewFromDay { get; }

    int ReviewToDay { get; }

    /// <summary>The settings as a short JSON text for the audit trail of a forced fetch (no secrets are in it).</summary>
    string Snapshot();
}
