namespace XDrishti.DhanTrial;

internal sealed record TokenEvent(DateTimeOffset At, string Step, bool Ok, string Detail);

internal sealed record SymbolHistory(
    string Symbol,
    SymbolKind Kind,
    InstrumentKey? Key,
    string Window,
    int Chunks,
    int FromCache,
    IReadOnlyList<string> Errors,
    SymbolQuality? Quality,
    DailyComparison? Daily,
    IReadOnlyList<DateOnly> Discontinuities,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    int IntradayBars,
    int DailyBars);

internal sealed record DepthProbe(int YearsBack, int Bars, string? Error);

internal sealed record BurstResult(int Requests, int Succeeded, int Throttled, IReadOnlyDictionary<string, int> Errors);

internal sealed record EndpointStats(string Endpoint, int Calls, double MedianMs, double P95Ms, double MaxMs, IReadOnlyDictionary<string, int> Errors);

internal sealed record ErrorProbe(string Name, int Status, string? Code, string? Message, int Bars);

internal sealed record EodEstimate(int Instruments, double SequentialSeconds, double RateLimitedSeconds, int BackfillCallsPerInstrument, double BackfillSeconds);

/// <summary>Everything the report needs; filled by <see cref="TrialRunner"/>.</summary>
internal sealed class TrialResults
{
    public DateTimeOffset GeneratedAt { get; init; }

    public bool Offline { get; init; }

    public List<TokenEvent> TokenEvents { get; } = [];

    public ProfileInfo? Profile { get; set; }

    public IReadOnlyList<string> MasterHeader { get; set; } = [];

    public int MasterRows { get; set; }

    public int MasterNseRows { get; set; }

    public List<Resolution> Resolutions { get; } = [];

    public TimestampSemantics? Semantics { get; set; }

    public string? DailyEpochNote { get; set; }

    public List<SymbolHistory> History { get; } = [];

    public List<DepthProbe> Depth { get; } = [];

    public BurstResult? Burst { get; set; }

    public List<EndpointStats> Endpoints { get; } = [];

    public EodEstimate? Estimate { get; set; }

    public List<ErrorProbe> Probes { get; } = [];

    public string? IntervalForm { get; set; }

    public int MaxBarsPerRequest { get; set; }

    public bool BurstRequested { get; init; }

    public List<string> Notes { get; } = [];
}
