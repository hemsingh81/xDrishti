namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary><c>Data:History:Lists:*</c> (DATA-01, ADR 0274): the dated ban-list archive fetch. Off by default; off means zero HTTP calls.</summary>
public interface IListHistorySettings
{
    /// <summary><c>Data:History:Lists:Enabled</c>, default false.</summary>
    bool Enabled { get; }

    /// <summary>Most requests one run makes (default 500).</summary>
    int MaxPerDay { get; }

    /// <summary>The wait between two requests.</summary>
    TimeSpan Delay { get; }
}
