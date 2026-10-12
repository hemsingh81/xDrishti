using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// One run of the data quality checks. <c>trigger</c>: manual · daily (the scheduled end-of-day run) · after_job (a data job the owner started with the quality step). <c>status</c>: running · succeeded · failed.
/// <c>instruments</c> is how many the run checks, <c>passed</c>, <c>warned</c> and <c>failed</c> how many it has checked so far with that result; <c>score</c> is the mean (0–100, once it ended).
/// <c>instrumentId</c> is set when the run checked one instrument only (a fix action); <c>jobId</c> is the data job it belongs to.
/// </summary>
public sealed record QualityRunView(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Sessions,
    int Instruments,
    int Passed,
    int Warned,
    int Failed,
    double? Score,
    string? Error,
    Guid? InstrumentId,
    Guid? JobId)
{
    public static QualityRunView From(QualityRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new QualityRunView(
            run.Id,
            run.Trigger.ToCode(),
            run.Status.ToString().ToLowerInvariant(),
            IndiaTime.ToIst(run.StartedAt),
            run.FinishedAt is { } finished ? IndiaTime.ToIst(finished) : null,
            run.Sessions,
            run.Instruments,
            run.Passed,
            run.Warned,
            run.Failed,
            run.Score,
            run.Error,
            run.InstrumentId,
            run.JobId);
    }
}

/// <summary>
/// The current results over the tracked instruments that have bars: how many there are (<c>tracked</c>), how many were <c>checked</c> (have a result), the counts by result, <c>notChecked</c>, and the mean score
/// of the results (null before the first run). The current result of an instrument is the one of the latest run that checked it.
/// </summary>
public sealed record QualityCounts(int Tracked, int Checked, int Passed, int Warned, int Failed, int NotChecked, double? Score);

/// <summary>
/// The gate for the nightly plan (fail-closed). <c>blocked</c>: no run yet, the last run over every instrument failed, or more than <c>maxFailedPercent</c> of the checked instruments fail;
/// <c>ok</c>: not blocked and no instrument fails; <c>reasons</c> says why it is not ok. Instruments that fail individually are skipped by the scan whatever the gate says.
/// </summary>
public sealed record QualityGateView(bool Ok, bool Blocked, IReadOnlyList<string> Reasons, int CheckedInstruments, int FailedInstruments, double FailedPercent, double MaxFailedPercent, DateTimeOffset? LastRunAt);

/// <summary>The window of the checks: how many closed sessions, from which date to which (the ones a run started now would look at).</summary>
public sealed record QualityWindowView(int Sessions, DateOnly? From, DateOnly? To);

/// <summary>
/// What the Data quality screen shows above the table: <c>lastRun</c> (the latest run over every instrument, null before the first), the <c>counts</c> of the current results with the mean <c>score</c>,
/// the <c>gate</c>, the <c>window</c>, the limits in force (<c>spikeMultiple</c>, <c>maxMissingPercent</c>) and <c>pendingCorporateActions</c>: the suggested splits and bonus issues waiting for the owner (ADR 0017 §5).
/// </summary>
public sealed record QualityOverview(QualityRunView? LastRun, QualityCounts Counts, QualityGateView Gate, QualityWindowView Window, double SpikeMultiple, double MaxMissingPercent, int PendingCorporateActions = 0);

/// <summary>The status of each check of an instrument: pass · warning · fail · notAvailable (<c>officialDaily</c>).</summary>
public sealed record QualityCheckStatuses(string Completeness, string Sanity, string Spikes, string Aggregates, string Freshness, string OfficialDaily);

/// <summary>
/// An instrument in the quality table. <c>status</c>: pass · warning · fail · notChecked (never checked). <c>coverage</c> is the share of the expected minutes that are stored (percent); <c>aggregates</c> gives the status
/// of each timeframe (5m … 1M); <c>checks</c> the status of each check; <c>issues</c> says in words what is wrong; <c>source</c>: csv · dhan · mixed.
/// </summary>
public sealed record QualityInstrumentRow(
    Guid InstrumentId,
    string Symbol,
    string Name,
    string Type,
    string Status,
    int? Score,
    double? Coverage,
    int? BarsChecked,
    int? MissingMinutes,
    QualityCheckStatuses? Checks,
    IReadOnlyDictionary<string, string>? Aggregates,
    IReadOnlyList<string> Issues,
    string? Source,
    DateTimeOffset? CheckedAt,
    Guid? RunId);

public sealed record QualityInstrumentPage(IReadOnlyList<QualityInstrumentRow> Items, int Page, int PageSize, int Total);

/// <summary>The checks of one instrument as the last run found them (without the long lists, which the detail gives separately).</summary>
public sealed record QualityCheckResults(CompletenessCheck Completeness, SanityCheck Sanity, SpikeCheck Spikes, AggregatesCheck Aggregates, FreshnessCheck Freshness, OfficialDailyCheck OfficialDaily);

/// <summary>A missing minute: the India date and the minute (<c>HH:mm</c>).</summary>
public sealed record MissingMinute(DateOnly Date, string Time);

/// <summary>The missing minutes of the window: <c>total</c> of them, and the first <c>items</c> (at most 500; <c>truncated</c> when there are more).</summary>
public sealed record QualityMissing(int Total, bool Truncated, IReadOnlyList<MissingMinute> Items);

/// <summary>The values of a bucket: open, high, low, close, volume and the number of 1-minute bars in it.</summary>
public sealed record QualityBucketValues(double Open, double High, double Low, double Close, long Volume, int Bars);

/// <summary>A bucket stored and recomputed side by side; <c>fields</c> name what differs (open, high, low, close, volume, bars) or <c>missing</c> / <c>unexpected</c>; <c>stored</c> or <c>recomputed</c> is null when there is none.</summary>
public sealed record QualityBucketRow(string Timeframe, DateTimeOffset Ts, QualityBucketValues? Stored, QualityBucketValues? Recomputed, bool Differs, IReadOnlyList<string> Fields);

/// <summary>
/// The stored and the recomputed buckets of the window, computed when asked (<c>computedAt</c>), so they show the state now, whatever the last run saw. <c>total</c> and <c>differences</c> count all buckets of the
/// request's timeframe filter; <c>items</c> are cut at 2000 (<c>truncated</c>); with <c>differencesOnly</c> only the differing ones are listed.
/// </summary>
public sealed record QualityBuckets(DateTimeOffset ComputedAt, int Total, int Differences, bool Truncated, IReadOnlyList<QualityBucketRow> Items);

/// <summary>
/// One instrument's quality: the current result (<c>status</c> pass · warning · fail · notChecked, <c>score</c>, <c>checks</c> from the run <c>runId</c> at <c>checkedAt</c>), the missing minutes of that run
/// and the stored-vs-recomputed buckets as they are now.
/// </summary>
public sealed record QualityInstrumentDetail(
    Guid InstrumentId,
    string Symbol,
    string Name,
    string Type,
    bool Tracked,
    string? Source,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    string Status,
    int? Score,
    int? BarsChecked,
    Guid? RunId,
    DateTimeOffset? CheckedAt,
    QualityCheckResults? Checks,
    QualityMissing Missing,
    QualityBuckets Buckets);

/// <summary>The instruments list as received (query string values, unchecked).</summary>
public sealed record QualityInstrumentRequest(string? Q, string? Status, string? Sort, int? Page, int? PageSize);

public enum QualitySortField
{
    Symbol,
    Name,
    Status,
    Score,
    Coverage,
    BarsChecked,
    MissingMinutes,
    CheckedAt,
}

/// <summary>A checked search of the quality table: <c>Status</c> is pass, warning, fail or notChecked (null: all).</summary>
public sealed record QualityInstrumentSearch(string? Text, string? Status, QualitySortField SortField, bool Descending, int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxTextLength = 100;

    public const int MaxPage = 100_000;

    public const string NotChecked = "notChecked";
}
