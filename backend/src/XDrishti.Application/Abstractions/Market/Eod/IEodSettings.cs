namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary><c>Data:History:Nse:*</c> (CF-04): all default OFF or safe.</summary>
public interface IEodSettings
{
    /// <summary><c>Data:History:Nse:Enabled</c>, default false. Off means zero HTTP calls.</summary>
    bool Enabled { get; }

    /// <summary>The series that are stored (default EQ).</summary>
    IReadOnlyList<string> Series { get; }

    /// <summary>Most requests one run makes (default 2,000).</summary>
    int MaxPerDay { get; }

    /// <summary>The wait between two requests.</summary>
    TimeSpan Delay { get; }

    /// <summary>How many years back the job loads (default 10).</summary>
    int Years { get; }

    /// <summary>Tolerance of the daily-versus-minute volume finding, in percent (default 2).</summary>
    double VolumeTolerancePct { get; }
}
