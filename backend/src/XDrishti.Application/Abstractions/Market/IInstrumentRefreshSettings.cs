namespace XDrishti.Application.Abstractions.Market;

/// <summary>Configuration of the master-list refresh (<c>Instruments:AutoRefresh</c>).</summary>
public interface IInstrumentRefreshSettings
{
    /// <summary>Whether the daily automatic refresh runs.</summary>
    bool AutoRefreshEnabled { get; }

    /// <summary>Time of day, India time, of the automatic refresh.</summary>
    TimeOnly AutoRefreshTimeIst { get; }
}
