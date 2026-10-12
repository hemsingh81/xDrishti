using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// One run of the data pipeline (ADR 0016 §2): the phases it was asked for (CSV files, Dhan fetch, aggregates), how far each is and what it did. Recorded
/// for every run, including failed ones; at most one runs at any time. A phase that was not asked for stays <see cref="PhaseStatus.NotRequested"/>.
/// </summary>
public sealed class DataJob : Entity<Guid>
{
    public const int MaxWarnings = 50;
    public const int MaxWarningLength = 300;
    public const int MaxErrorLength = 1000;
    public const int MaxRequestedByLength = 256;

    private const string NotRun = "Not run: the update stopped before this step.";

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private DataJob(Guid id, string requestedBy, DateTimeOffset startedAt, IReadOnlyList<string> steps, bool forceCsv, bool rebuildAll, DataJobTrigger trigger, bool refetchGaps, Guid? instrumentId, IReadOnlyList<string>? csvFiles, string? undoCsvFile)
        : base(id)
    {
        CsvFiles = csvFiles;
        UndoCsvFile = undoCsvFile;
        RequestedBy = requestedBy;
        Trigger = trigger;
        RefetchGaps = refetchGaps;
        InstrumentId = instrumentId;
        StartedAt = startedAt;
        Steps = steps;
        ForceCsv = forceCsv;
        RebuildAll = rebuildAll;
        Status = DataJobStatus.Running;
        Csv = CsvPhase.Empty(steps.Contains(DataSteps.Csv) ? PhaseStatus.Pending : PhaseStatus.NotRequested);
        Dhan = DhanPhase.Empty(steps.Contains(DataSteps.Dhan) ? PhaseStatus.Pending : PhaseStatus.NotRequested);
        Aggregate = AggregatePhase.Empty(steps.Contains(DataSteps.Aggregate) ? PhaseStatus.Pending : PhaseStatus.NotRequested) with { Full = rebuildAll };
        Quality = QualityPhase.Empty(steps.Contains(DataSteps.Quality) ? PhaseStatus.Pending : PhaseStatus.NotRequested);
    }

    public string RequestedBy { get; private set; }

    /// <summary>What started the job: the owner or the scheduled end-of-day run.</summary>
    public DataJobTrigger Trigger { get; private set; }

    /// <summary>The Dhan phase also retries the gaps it had given up on (<see cref="GapStatus.Unfillable"/>); only meaningful with the Dhan step.</summary>
    public bool RefetchGaps { get; private set; }

    /// <summary>
    /// The one instrument a job is about (the fix actions of the Data quality screen: re-fetch, refresh aggregates, re-check one instrument); null for a job over every
    /// tracked instrument. Such a job is not counted as the last run of a phase.
    /// </summary>
    public Guid? InstrumentId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public DataJobStatus Status { get; private set; }

    /// <summary>The phases asked for, in the order they run.</summary>
    public IReadOnlyList<string> Steps { get; private set; }

    /// <summary>Re-import CSV files that were loaded already and have not changed.</summary>
    public bool ForceCsv { get; private set; }

    /// <summary>The CSV files the CSV phase loads (names from the folder listing, validated when the job was started); null means every file of the folder. Only set for a job with the CSV step.</summary>
    public IReadOnlyList<string>? CsvFiles { get; private set; }

    /// <summary>
    /// The file whose load this job undoes (ADR 0017 §6): its CSV phase removes the file's CSV bars instead of loading files, then the aggregate phase rebuilds the instrument. Null for every other job.
    /// </summary>
    public string? UndoCsvFile { get; private set; }

    /// <summary>The aggregates are rebuilt for everything, not just the ranges touched (the job asked for no phase that writes bars).</summary>
    public bool RebuildAll { get; private set; }

    /// <summary>The phase in progress (one of <see cref="DataSteps"/>); null before the first one starts and after the job ended.</summary>
    public string? CurrentPhase { get; private set; }

    public CsvPhase Csv { get; private set; }

    public DhanPhase Dhan { get; private set; }

    public AggregatePhase Aggregate { get; private set; }

    public QualityPhase Quality { get; private set; }

    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsRunning => Status == DataJobStatus.Running;

    public bool Requested(string step) => Steps.Contains(step);

    /// <summary>
    /// Registers a job for the given steps (unknown ones are ignored; duplicates collapse; they always run in pipeline order). <paramref name="instrumentId"/> limits the Dhan,
    /// aggregate and quality steps to one instrument.
    /// </summary>
    public static DataJob Start(
        IEnumerable<string> steps,
        bool forceCsv,
        string requestedBy,
        DateTimeOffset now,
        DataJobTrigger trigger = DataJobTrigger.Manual,
        bool refetchGaps = false,
        Guid? instrumentId = null,
        IReadOnlyList<string>? csvFiles = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var chosen = steps.ToHashSet(StringComparer.Ordinal);
        var ordered = DataSteps.All.Where(chosen.Contains).ToList();
        if (ordered.Count == 0)
        {
            throw new ArgumentException("A data job needs at least one step.", nameof(steps));
        }

        var rebuildAll = ordered.Contains(DataSteps.Aggregate) && !ordered.Contains(DataSteps.Csv) && !ordered.Contains(DataSteps.Dhan);
        return new DataJob(
            Guid.CreateVersion7(now),
            string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : Limit(requestedBy.Trim(), MaxRequestedByLength)!,
            now,
            ordered,
            forceCsv && ordered.Contains(DataSteps.Csv),
            rebuildAll,
            trigger,
            refetchGaps && ordered.Contains(DataSteps.Dhan),
            instrumentId,
            csvFiles is { Count: > 0 } && ordered.Contains(DataSteps.Csv) ? [.. csvFiles.Distinct(StringComparer.Ordinal)] : null,
            null);
    }

    /// <summary>
    /// The job that undoes the load of one CSV file (ADR 0017 §6): the CSV step removes that file's bars of <paramref name="instrumentId"/>, the aggregate step rebuilds the instrument.
    /// </summary>
    public static DataJob StartUndo(string fileName, Guid instrumentId, string requestedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var job = Start([DataSteps.Csv, DataSteps.Aggregate], false, requestedBy, now, DataJobTrigger.Manual, false, instrumentId);
        job.UndoCsvFile = fileName;
        return job;
    }

    /// <summary>
    /// The record of a run that did not start (ADR 0017 §2: the scheduled run found another job running, or the Data account not connected): status
    /// <see cref="DataJobStatus.Skipped"/>, ended at once, <paramref name="reason"/> as the error. No phase ran, so every phase stays <see cref="PhaseStatus.NotRequested"/> and
    /// the job never counts as the last run of a phase.
    /// </summary>
    public static DataJob Skipped(IEnumerable<string> steps, DataJobTrigger trigger, string requestedBy, string reason, DateTimeOffset now)
    {
        var job = Start(steps, false, requestedBy, now, trigger);
        job.Status = DataJobStatus.Skipped;
        job.FinishedAt = now;
        job.Error = Limit(reason, MaxErrorLength);
        job.Csv = CsvPhase.Empty(PhaseStatus.NotRequested);
        job.Dhan = DhanPhase.Empty(PhaseStatus.NotRequested);
        job.Aggregate = AggregatePhase.Empty(PhaseStatus.NotRequested);
        job.Quality = QualityPhase.Empty(PhaseStatus.NotRequested);
        return job;
    }

    /// <summary>Records how far a phase is; the running phase becomes the current one. Nothing happens once the job has ended.</summary>
    public void Report(IDataPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        if (!IsRunning)
        {
            return;
        }

        switch (phase)
        {
            case CsvPhase csv:
                Csv = csv;
                CurrentPhase = phase.Status == PhaseStatus.Running ? DataSteps.Csv : CurrentPhase;
                break;
            case DhanPhase dhan:
                Dhan = dhan;
                CurrentPhase = phase.Status == PhaseStatus.Running ? DataSteps.Dhan : CurrentPhase;
                break;
            case AggregatePhase aggregate:
                Aggregate = aggregate;
                CurrentPhase = phase.Status == PhaseStatus.Running ? DataSteps.Aggregate : CurrentPhase;
                break;
            case QualityPhase quality:
                Quality = quality;
                CurrentPhase = phase.Status == PhaseStatus.Running ? DataSteps.Quality : CurrentPhase;
                break;
            default:
                throw new ArgumentException("Unknown phase type.", nameof(phase));
        }
    }

    public void AddWarning(string warning)
    {
        if (!IsRunning || Warnings.Count > MaxWarnings)
        {
            return;
        }

        var kept = new List<string>(Warnings);
        kept.Add(Warnings.Count == MaxWarnings ? "…and more warnings." : Limit(warning, MaxWarningLength)!);
        Warnings = kept;
    }

    /// <summary>Ends the job: failed when a phase failed (the first failure is the error), otherwise succeeded.</summary>
    public void Finish(DateTimeOffset now)
    {
        var failed = new IDataPhase[] { Csv, Dhan, Aggregate, Quality }.FirstOrDefault(p => p.Status == PhaseStatus.Failed);
        End(now, failed is null ? DataJobStatus.Succeeded : DataJobStatus.Failed);
        Error = failed is null ? null : Limit(failed.Message ?? "A phase failed.", MaxErrorLength);
    }

    /// <summary>Ends the job as failed (crash, interruption): a phase still running is failed too and the ones not started are skipped.</summary>
    public void Fail(DateTimeOffset now, string error)
    {
        End(now, DataJobStatus.Failed);
        Error = Limit(error, MaxErrorLength);
        Csv = Csv.Status switch
        {
            PhaseStatus.Running => Csv with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Error },
            PhaseStatus.Pending => Csv with { Status = PhaseStatus.Skipped, Message = NotRun },
            _ => Csv,
        };
        Dhan = Dhan.Status switch
        {
            PhaseStatus.Running => Dhan with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Error },
            PhaseStatus.Pending => Dhan with { Status = PhaseStatus.Skipped, Message = NotRun },
            _ => Dhan,
        };
        Aggregate = Aggregate.Status switch
        {
            PhaseStatus.Running => Aggregate with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Error },
            PhaseStatus.Pending => Aggregate with { Status = PhaseStatus.Skipped, Message = NotRun },
            _ => Aggregate,
        };
        Quality = Quality.Status switch
        {
            PhaseStatus.Running => Quality with { Status = PhaseStatus.Failed, FinishedAt = now, Message = Error },
            PhaseStatus.Pending => Quality with { Status = PhaseStatus.Skipped, Message = NotRun },
            _ => Quality,
        };
    }

    private void End(DateTimeOffset now, DataJobStatus status)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The data job has already ended.");
        }

        Status = status;
        FinishedAt = now;
        CurrentPhase = null;
    }

    private static string? Limit(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}
