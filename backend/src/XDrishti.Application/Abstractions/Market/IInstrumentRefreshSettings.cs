namespace XDrishti.Application.Abstractions.Market;

/// <summary>Configuration of the master-list refresh (<c>Instruments:AutoRefresh</c>): the first-start settings of the instrument master service (ADR 0018), read once when it has no stored row.</summary>
public interface IInstrumentRefreshSettings
{
    /// <summary>Whether the daily automatic refresh runs.</summary>
    bool AutoRefreshEnabled { get; }

    /// <summary>Time of day, India time, of the automatic refresh.</summary>
    TimeOnly AutoRefreshTimeIst { get; }
}
