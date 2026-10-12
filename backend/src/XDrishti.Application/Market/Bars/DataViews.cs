using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// One data job (a run of the pipeline, ADR 0016). <c>status</c>: running · succeeded · failed. <c>currentPhase</c> (while running): csv · dhan · aggregate.
/// Each phase block has <c>status</c> (notRequested · pending · running · succeeded · skipped · failed), <c>done</c> of <c>total</c> (files, instruments,
/// instruments), <c>message</c> and <c>code</c> (why it was skipped or failed) and its counts.
/// </summary>
public sealed record DataJobView(
    Guid Id,
    string Status,
    string RequestedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<string> Steps,
    bool ForceCsv,
    bool RebuildAll,
    string? CurrentPhase,
    CsvPhase Csv,
    DhanPhase Dhan,
    AggregatePhase Aggregate,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static DataJobView From(DataJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new DataJobView(
            job.Id,
            job.Status.ToString().ToLowerInvariant(),
            job.RequestedBy,
            job.StartedAt,
            job.FinishedAt,
            job.Steps,
            job.ForceCsv,
            job.RebuildAll,
            job.CurrentPhase,
            job.Csv,
            job.Dhan,
            job.Aggregate,
            job.Warnings,
            job.Error);
    }
}

/// <summary>Starts a data job. <c>steps</c>: any of csv · dhan · aggregate (default all three, always run in that order); <c>forceCsv</c> loads CSV files again that were loaded already and have not changed.</summary>
public sealed record StartDataJobRequest(IReadOnlyList<string>? Steps = null, bool? ForceCsv = null);

/// <summary>
/// The CSV folder: the files that match <c>&lt;SYMBOL&gt;_1m.csv[.gz]</c>, how many are loaded (unchanged since they were loaded) or pending, the rows loaded so far and when the last file was loaded.
/// </summary>
public sealed record CsvOverview(string Folder, bool FolderExists, int FilesTotal, int FilesLoaded, int FilesPending, long Rows, DateTimeOffset? LastLoadAt);

/// <summary>The primary Data account. <c>status</c>: connected · expired · notConnected.</summary>
public sealed record DataAccountView(Guid Id, string Name, string Status);

/// <summary>
/// The Dhan Data API phase: the primary Data account (null when there is none), whether a fetch can run now (<c>canFetch</c>) and, when not, why
/// (<c>reason</c> in words, <c>reasonCode</c> data.dhan_not_available · data.dhan_not_connected), the settings, and the last successful fetch (time and bars added).
/// </summary>
public sealed record DhanOverview(DataAccountView? Account, bool CanFetch, string? Reason, string? ReasonCode, int InitialDays, int RequestsPerSecond, DateTimeOffset? LastFetchAt, long? LastFetchBars);

/// <summary>A derived timeframe (<c>5m 10m 15m 30m 1h 1D 1W 1M</c>): the bars stored and the start of the newest one.</summary>
public sealed record TimeframeStat(string Timeframe, long Bars, DateTimeOffset? LastBucket);

/// <summary>The derived timeframes: per timeframe, when they were last built and how many instruments have changes that are not aggregated yet.</summary>
public sealed record AggregatesOverview(IReadOnlyList<TimeframeStat> Timeframes, DateTimeOffset? LastBuiltAt, int InstrumentsPending);

/// <summary>
/// The stored 1-minute bars: instruments with bars, the first bar and the last bar (the "last tick"), how many weekday sessions the last bar is behind the last
/// closed session (<c>expectedSession</c>), <c>stale</c> (behind) and how many instruments are behind or still empty (tracked without bars).
/// </summary>
public sealed record BarsOverview(int Instruments, int InstrumentsBehind, int InstrumentsEmpty, long TotalBars, DateTimeOffset? FirstBar, DateTimeOffset? LastBar, DateOnly ExpectedSession, int? DaysBehind, bool Stale);

/// <summary>The latest job in which each phase ran to an end (null when none did yet).</summary>
public sealed record LastDataJobs(DataJobView? Csv, DataJobView? Dhan, DataJobView? Aggregate);

/// <summary>Everything the Data &amp; import screen shows above the table: the three phases, the stored bars and the job in progress.</summary>
public sealed record DataOverview(CsvOverview Csv, DhanOverview Dhan, AggregatesOverview Aggregates, BarsOverview Bars, DataJobView? RunningJob, LastDataJobs LastJobs);

/// <summary>
/// An instrument's coverage. <c>source</c>: csv · dhan · mixed (null without bars). <c>status</c>: current · behind · empty (tracked, no bars).
/// <c>daysBehind</c> counts weekday sessions (null without bars).
/// </summary>
public sealed record DataInstrumentRow(
    Guid Id,
    string Symbol,
    string Name,
    string Type,
    bool Tracked,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    long Bars,
    string? Source,
    string Status,
    int? DaysBehind);

public sealed record DataInstrumentPage(IReadOnlyList<DataInstrumentRow> Items, int Page, int PageSize, int Total);

/// <summary>One bar. Times of the day timeframes (<c>1D 1W 1M</c>) are India midnight of the first day of the bucket. <c>bars</c> counts the 1-minute bars in it; <c>complete</c> says none was missing. <c>source</c> (1m only): csv · dhan.</summary>
public sealed record BarRow(DateTimeOffset Ts, double Open, double High, double Low, double Close, long Volume, long Bars, bool Complete, string? Source);

/// <summary>The latest bars of an instrument and timeframe, newest first.</summary>
public sealed record InstrumentBars(Guid InstrumentId, string Symbol, string Name, string Timeframe, IReadOnlyList<BarRow> Items);
