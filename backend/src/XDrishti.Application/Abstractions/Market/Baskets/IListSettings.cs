namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary><c>Baskets:System:Lists:*</c> (BK-05): safe defaults, the two NSE file fetches OFF. Off means neither file is ever requested; the lists read from the master are unaffected.</summary>
public interface IListSettings
{
    /// <summary><c>Baskets:System:Lists:FetchEnabled</c>, default false.</summary>
    bool FetchEnabled { get; }

    /// <summary>The India times of day at which the ban list is asked (default 19:00 and 08:30).</summary>
    IReadOnlyList<TimeOnly> BanRunTimes { get; }

    /// <summary>The India time of day at which the lot-size file is asked (default 08:50, after the master).</summary>
    TimeOnly LotSizeRunAt { get; }
}
