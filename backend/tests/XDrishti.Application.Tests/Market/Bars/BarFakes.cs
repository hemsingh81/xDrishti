using System.IO.Compression;
using System.Text;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>The CSV folder in memory.</summary>
internal sealed class FakeCsvFolder : ICsvFolder
{
    private readonly Dictionary<string, (byte[] Content, DateTimeOffset Modified)> _files = new(StringComparer.Ordinal);

    public string Path => "/data/csv";

    public bool FolderExists { get; set; } = true;

    public List<string> Opened { get; } = [];

    public bool Exists() => FolderExists;

    /// <summary>When set, listing the folder throws it (an unreadable mount).</summary>
    public Exception? ListFailure { get; set; }

    public ValueTask<IReadOnlyList<CsvFileInfo>> ListAsync(CancellationToken cancellationToken) =>
        ListFailure is { } failure
            ? throw failure
            : ValueTask.FromResult<IReadOnlyList<CsvFileInfo>>([.. _files.Select(f => new CsvFileInfo(f.Key, f.Value.Content.Length, f.Value.Modified))]);

    /// <summary>Runs when a file is opened: what happens meanwhile (a test moves the clock).</summary>
    public Action<string>? OnOpen { get; set; }

    public Stream Open(string name)
    {
        Opened.Add(name);
        OnOpen?.Invoke(name);
        return new MemoryStream(_files[name].Content);
    }

    public CsvFileInfo Add(string name, string text, DateTimeOffset? modified = null, bool corruptGzip = false)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        byte[] content;
        if (corruptGzip)
        {
            content = [1, 2, 3, 4];
        }
        else if (name.EndsWith(".gz", StringComparison.Ordinal))
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(raw);
            }

            content = output.ToArray();
        }
        else
        {
            content = raw;
        }

        _files[name] = (content, modified ?? new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero));
        return new CsvFileInfo(name, content.Length, _files[name].Modified);
    }

    public static string Csv(int rows, long start = 1_711_943_100) =>
        "time,open,high,low,close,CE Breakout Level,PE Breakout Level,Volume\n" +
        string.Join('\n', Enumerable.Range(0, rows).Select(i => $"{start + i * 60L},100,101,99,100.5,,,10")) + "\n";
}

internal sealed class FakeCsvFileLog : ICsvFileLog
{
    public Dictionary<string, LoadedCsvFile> Loaded { get; } = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyDictionary<string, LoadedCsvFile>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, LoadedCsvFile>>(Loaded);

    public void Add(CsvFileInfo file, long rows = 1, DateTimeOffset? loadedAt = null) =>
        Loaded[file.Name] = new LoadedCsvFile(file.Name, file.Size, file.ModifiedAt, null, rows, rows, 0, 0, 0, loadedAt ?? new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
}

internal sealed class FakeListedInstruments : IListedInstruments
{
    public Dictionary<string, IReadOnlyList<Guid>> Matches { get; } = new(StringComparer.Ordinal);

    public Guid Add(string canonical)
    {
        var id = Guid.CreateVersion7();
        Matches[canonical] = [id];
        return id;
    }

    public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<Guid>>>(Matches.Where(m => canonicalSymbols.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal));
}

/// <summary>Merges in memory: every bar added counts as inserted (the real precedence and counts are the database's, tested against it).</summary>
internal sealed class FakeBarStore : IBarStore
{
    public List<FakeSession> Sessions { get; } = [];

    /// <summary>Instruments whose merge throws, and what.</summary>
    public Dictionary<Guid, Exception> Failures { get; } = [];

    public Dictionary<Guid, List<Bar>> Stored { get; } = [];

    /// <summary>Every <see cref="ReadAsync"/> in the order it was called (the simulation tests assert the order, the window and the offset of the bounds).</summary>
    public List<(Guid InstrumentId, DateTimeOffset From, DateTimeOffset To)> Reads { get; } = [];

    /// <summary>When set, a read really awaits (a yield) between its start and its end, so that two reads in flight at once can be observed.</summary>
    public bool AwaitReads { get; set; }

    /// <summary>The most reads that were in flight at the same time.</summary>
    public int MaxConcurrentReads { get; private set; }

    /// <summary>Runs at the start of every read, with the instrument: what a caller does meanwhile (a test cancels its token).</summary>
    public Action<Guid>? OnRead { get; set; }

    /// <summary>When set, <see cref="BeginMergeAsync"/> throws: a reader of the bars must never merge.</summary>
    public bool ForbidMerge { get; set; }

    private int _inFlight;

    public ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken)
    {
        if (ForbidMerge)
        {
            throw new InvalidOperationException("A merge was begun, and this test forbids it.");
        }

        var session = new FakeSession(this, instrumentId, source);
        Sessions.Add(session);
        return ValueTask.FromResult<IBarMergeSession>(session);
    }

    public async ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        Reads.Add((instrumentId, from, to));
        MaxConcurrentReads = Math.Max(MaxConcurrentReads, Interlocked.Increment(ref _inFlight));
        try
        {
            OnRead?.Invoke(instrumentId);
            if (AwaitReads)
            {
                await Task.Yield();
            }

            return [.. Stored.GetValueOrDefault(instrumentId, []).Where(b => b.Start >= from && b.Start <= to).OrderBy(b => b.EpochSeconds)];
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    internal sealed class FakeSession(FakeBarStore store, Guid instrumentId, BarSource source) : IBarMergeSession
    {
        public Guid InstrumentId => instrumentId;

        public BarSource Source => source;

        public List<Bar> Bars { get; } = [];

        public CsvFileMark? Mark { get; private set; }

        public bool Completed { get; private set; }

        public ValueTask AddAsync(ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken)
        {
            Bars.AddRange(bars.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<BarMergeResult> CompleteAsync(CsvFileMark? file, CancellationToken cancellationToken)
        {
            if (store.Failures.TryGetValue(instrumentId, out var failure))
            {
                throw failure;
            }

            Completed = true;
            Mark = file;
            if (!store.Stored.TryGetValue(instrumentId, out var list))
            {
                store.Stored[instrumentId] = list = [];
            }

            list.AddRange(Bars);
            return ValueTask.FromResult(new BarMergeResult(Bars.Count, 0, 0, 0));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeDataJobStore : IDataJobStore
{
    public Dictionary<Guid, DataJob> Jobs { get; } = [];

    /// <summary>The state of the job at every publish, as JSON-free snapshots: (status, csv done, dhan done, aggregate done, current phase).</summary>
    public List<(DataJobStatus Status, long CsvDone, long DhanDone, long AggregateDone, string? Phase)> Published { get; } = [];

    public List<DataJob> Started { get; } = [];

    public Dictionary<string, DataJob> Last { get; } = [];

    /// <summary>Runs at the start of every <see cref="TryStartAsync"/>: what a concurrent request would have done meanwhile.</summary>
    public Action? OnTryStart { get; set; }

    public ValueTask<bool> TryStartAsync(DataJob job, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        OnTryStart?.Invoke();
        if (Jobs.Values.Any(j => j.IsRunning))
        {
            return ValueTask.FromResult(false);
        }

        Jobs[job.Id] = job;
        Started.Add(job);
        return ValueTask.FromResult(true);
    }

    public ValueTask<DataJob?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Jobs.GetValueOrDefault(id));

    /// <summary>Jobs that were ended elsewhere (failed by recovery or the stale rule) while a worker was busy: publishing them writes nothing and returns false, as the store does.</summary>
    public HashSet<Guid> EndedElsewhere { get; } = [];

    public ValueTask<bool> PublishAsync(DataJob job, CancellationToken cancellationToken)
    {
        if (EndedElsewhere.Contains(job.Id))
        {
            return ValueTask.FromResult(false);
        }

        Published.Add((job.Status, job.Csv.Done, job.Dhan.Done, job.Aggregate.Done, job.CurrentPhase));
        return ValueTask.FromResult(true);
    }

    public ValueTask<DataJob?> GetRunningAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Jobs.Values.FirstOrDefault(j => j.IsRunning));

    public ValueTask<DataJob?> GetLastWithPhaseAsync(string step, IReadOnlyCollection<string> statuses, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Jobs.Values.OrderByDescending(j => j.StartedAt).FirstOrDefault(j => statuses.Contains(step switch { DataSteps.Csv => j.Csv.Status, DataSteps.Dhan => j.Dhan.Status, _ => j.Aggregate.Status })));

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = Jobs.Values.Where(j => j.IsRunning && j.StartedAt < processStartedAt).ToList();
        foreach (var job in running)
        {
            job.Fail(now, "interrupted");
        }

        return ValueTask.FromResult(running.Count);
    }

    public ValueTask<IReadOnlyList<DataJob>> ListAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DataJob>>([.. Jobs.Values.OrderByDescending(j => j.StartedAt).Take(take)]);

    /// <summary>The jobs recorded as skipped (they are in <see cref="Jobs"/> too).</summary>
    public List<DataJob> Skipped { get; } = [];

    /// <summary>When set, reading the latest job of a trigger throws it (the database is down).</summary>
    public Exception? LastByTriggerFailure { get; set; }

    public ValueTask<DataJob?> GetLastByTriggerAsync(DataJobTrigger trigger, CancellationToken cancellationToken) =>
        LastByTriggerFailure is { } failure
            ? throw failure
            : ValueTask.FromResult(Jobs.Values.Where(j => j.Trigger == trigger).OrderByDescending(j => j.StartedAt).FirstOrDefault());

    public ValueTask RecordSkippedAsync(DataJob skipped, CancellationToken cancellationToken)
    {
        Jobs[skipped.Id] = skipped;
        Skipped.Add(skipped);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDataSettings : IDataSettings
{
    public string CsvFolder { get; set; } = "/data/csv";

    public int DhanInitialDays { get; set; } = 365;

    public int DhanInitialDaysIndex { get; set; } = 1825;

    public int DhanRequestsPerSecond { get; set; } = 4;

    public int CsvMaxRowsPerFile { get; set; } = 5_000_000;

    public long CsvMaxBytesPerFile { get; set; } = 1L << 30;

    public int CsvMaxSpanDays { get; set; } = 3_700;

    public int GapLookbackSessions { get; set; } = 30;

    public bool EodEnabled { get; set; } = true;

    public TimeOnly EodTimeIst { get; set; } = new(15, 50);

    public int QualitySessions { get; set; } = 5;

    public double QualitySpikeMultiple { get; set; } = 20;

    public double QualityMaxMissingPercent { get; set; } = 1;

    public double QualityMaxFailedPercent { get; set; } = 5;
}

internal sealed class FakeFetchTargets : IFetchTargets
{
    public List<FetchTarget> Targets { get; } = [];

    public ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<FetchTarget>>(Targets);
}

internal sealed class FakeAccountStatus(DataAccountState state) : IDataAccountStatus
{
    public DataAccountState State { get; set; } = state;

    public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(State);

    public ValueTask<XDrishti.Domain.Accounts.SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<XDrishti.Domain.Accounts.SecretValue?>(null);

    public static FakeAccountStatus Connected(string name = "Primary") => new(new DataAccountState(Guid.NewGuid(), name, DataAccountStatuses.Connected, null));
}

internal sealed class FakeIntradaySource : IIntradayBarSource
{
    public SourceAvailability Availability { get; set; } = SourceAvailability.Ready;

    public Func<IntradayTarget, IntradayResult> Respond { get; set; } = t => new IntradayResult(t.InstrumentId, [], 1);

    public List<IntradayFetchPlan> Plans { get; } = [];

    public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Availability);

    public async IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Plans.Add(plan);
        foreach (var target in plan.Targets)
        {
            await Task.Yield();
            var result = Respond(target);
            yield return result;
            if (result.Failure?.Kind == IntradayFailureKind.Unauthorized)
            {
                yield break;
            }
        }
    }
}

internal sealed class FakeAggregates : IBarAggregates
{
    public List<DirtyInstrument> Dirty { get; } = [];

    public bool MarkedAll { get; private set; }

    public List<DirtyInstrument> Built { get; } = [];

    public Exception? FailOn { get; set; }

    public ValueTask<int> MarkAllDirtyAsync(CancellationToken cancellationToken)
    {
        MarkedAll = true;
        return ValueTask.FromResult(Dirty.Count);
    }

    /// <summary>The instruments marked dirty from a time on, as the quality fix action does.</summary>
    public List<(Guid InstrumentId, DateTimeOffset From)> MarkedDirty { get; } = [];

    public ValueTask<bool> MarkDirtyAsync(Guid instrumentId, DateTimeOffset from, CancellationToken cancellationToken)
    {
        MarkedDirty.Add((instrumentId, from));
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyList<DirtyInstrument>> ListDirtyAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<DirtyInstrument>>([.. Dirty]);

    public ValueTask<AggregatesBuilt> BuildAsync(DirtyInstrument target, CancellationToken cancellationToken)
    {
        if (FailOn is not null)
        {
            throw FailOn;
        }

        Built.Add(target);
        return ValueTask.FromResult(new AggregatesBuilt(new Dictionary<string, long> { ["5m"] = 10, ["1D"] = 1 }));
    }
}

/// <summary>The gap records in memory.</summary>
internal sealed class FakeBarGaps : IBarGaps
{
    public Dictionary<(Guid Instrument, DateOnly Date), BarGap> Rows { get; } = [];

    public List<IReadOnlyCollection<BarGap>> Saves { get; } = [];

    public ValueTask<IReadOnlyList<BarGap>> ListForAsync(Guid instrumentId, DateOnly since, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BarGap>>([.. Rows.Values.Where(g => g.InstrumentId == instrumentId && (g.Status != GapStatus.Filled || g.Date >= since)).OrderBy(g => g.Date)]);

    public ValueTask SaveAsync(IReadOnlyCollection<BarGap> gaps, CancellationToken cancellationToken)
    {
        Saves.Add([.. gaps]);
        foreach (var gap in gaps)
        {
            Rows[(gap.InstrumentId, gap.Date)] = gap;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<GapSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GapSummary(
            Rows.Values.Count(g => g.Status == GapStatus.Open),
            Rows.Values.Count(g => g.Status == GapStatus.Unfillable),
            Rows.Values.Count(g => g.Status == GapStatus.Filled),
            Rows.Count == 0 ? null : Rows.Values.Max(g => g.DetectedAt)));

    public ValueTask<XDrishti.Application.Market.Bars.Gaps.GapPage> ListAsync(XDrishti.Application.Market.Bars.Gaps.GapSearch search, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new XDrishti.Application.Market.Bars.Gaps.GapPage([], search.Page, search.PageSize, 0));
}

/// <summary>
/// Bars per day: what a test presets (<see cref="Preset"/>, as the daily aggregates would hold) plus the bars that the fake bar store has merged since, counted by India day.
/// </summary>
internal sealed class FakeBarDayCounts(FakeBarStore store) : IBarDayCounts
{
    public Dictionary<Guid, Dictionary<DateOnly, int>> Preset { get; } = [];

    public List<(Guid Instrument, DateOnly From, DateOnly To)> Asked { get; } = [];

    public ValueTask<IReadOnlyDictionary<DateOnly, int>> CountAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Asked.Add((instrumentId, from, to));
        var counts = new Dictionary<DateOnly, int>(Preset.GetValueOrDefault(instrumentId) ?? []);
        foreach (var day in store.Stored.GetValueOrDefault(instrumentId, []).Select(b => SessionClock.DateOf(b.EpochSeconds)).GroupBy(d => d))
        {
            counts[day.Key] = counts.GetValueOrDefault(day.Key) + day.Count();
        }

        return ValueTask.FromResult<IReadOnlyDictionary<DateOnly, int>>(counts.Where(c => c.Key >= from && c.Key <= to).ToDictionary(c => c.Key, c => c.Value));
    }
}

/// <summary>The undo of a CSV load in memory: a file is "loaded" when a test adds it; undoing it removes it and reports the bars the test said it wrote.</summary>
internal sealed class FakeCsvUndo : ICsvUndo
{
    public Dictionary<string, CsvUndoResult> Loaded { get; } = new(StringComparer.Ordinal);

    public List<string> Undone { get; } = [];

    /// <summary>When set, undoing throws it (the database refused).</summary>
    public Exception? Failure { get; set; }

    public ValueTask<CsvUndoResult?> UndoAsync(string fileName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        if (!Loaded.Remove(fileName, out var result))
        {
            return ValueTask.FromResult<CsvUndoResult?>(null);
        }

        Undone.Add(fileName);
        return ValueTask.FromResult<CsvUndoResult?>(result);
    }
}

/// <summary>The comparison against stored bars in memory: what the test presets per instrument, the same for every batch (or computed by <see cref="Respond"/>).</summary>
internal sealed class FakeBarComparison : IBarComparison
{
    public Func<Guid, ReadOnlyMemory<Bar>, BarComparisonResult> Respond { get; set; } = (_, bars) => new BarComparisonResult(bars.Length, 0, 0, 0);

    public List<(Guid InstrumentId, int Bars)> Asked { get; } = [];

    public ValueTask<BarComparisonResult> CompareAsync(Guid instrumentId, ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken)
    {
        Asked.Add((instrumentId, bars.Length));
        return ValueTask.FromResult(Respond(instrumentId, bars));
    }
}
