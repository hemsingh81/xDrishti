namespace XDrishti.Application.Abstractions.Market.History;

/// <summary><c>Data:History:Backfill:*</c> and <c>Data:History:Probe:*</c> (CF-04 Part B): all default OFF or safe. The request rate is the existing <c>Data:Dhan:RequestsPerSecond</c> (<c>IDataSettings</c>).</summary>
public interface IBackfillSettings
{
    /// <summary><c>Data:History:Backfill:Enabled</c>, default false. Off means zero HTTP calls, from the service, the verbs and the probe alike.</summary>
    bool Enabled { get; }

    /// <summary><c>Data:History:Replay:AdmitVerified</c>, default false (CF-04d): a verified instrument's pre-seam bars enter a simulation. Off means every record, key and label is what it was before.</summary>
    bool AdmitVerified { get; }

    /// <summary>How many years back the backfill reaches (default 5, 1..5).</summary>
    int Years { get; }

    /// <summary>Days of one request (default 90, 7..90).</summary>
    int ChunkDays { get; }

    /// <summary>Attempts of one chunk before it stays <c>failed</c> (default 3).</summary>
    int MaxRetries { get; }

    /// <summary>The disk guard's factor (default 2) and the bytes per row it assumes (default 60).</summary>
    int DiskHeadroomFactor { get; }

    int BytesPerRowEstimate { get; }

    /// <summary>The off-hours window of the scheduled run, India time (default 20:00 to 06:00; it may cross midnight).</summary>
    TimeOnly RunWindowFrom { get; }

    TimeOnly RunWindowTo { get; }

    /// <summary>The probe's size and limits (defaults 20 instruments, 10 sessions, 5 bps median, 50 bps limit, 99 % within it).</summary>
    int ProbeInstruments { get; }

    int ProbeSessions { get; }

    double ProbeMedianBps { get; }

    double ProbeMaxBps { get; }

    double ProbeMinWithinMaxPct { get; }
}
