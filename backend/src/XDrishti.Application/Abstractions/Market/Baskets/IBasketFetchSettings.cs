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
