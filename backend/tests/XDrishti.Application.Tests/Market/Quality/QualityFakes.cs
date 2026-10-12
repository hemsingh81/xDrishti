using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Tests.Market.Quality;

/// <summary>The instruments to check and their stored aggregates, in memory.</summary>
internal sealed class FakeQualityData : IQualityData
{
    public List<QualityTarget> Targets { get; } = [];

    public Dictionary<Guid, IReadOnlyList<StoredBucket>> Stored { get; } = [];

    /// <summary>Instruments whose stored aggregates cannot be read (the database failing for them).</summary>
    public HashSet<Guid> Failing { get; } = [];

    public List<Guid?> Asked { get; } = [];

    public ValueTask<IReadOnlyList<QualityTarget>> ListTargetsAsync(Guid? instrumentId, CancellationToken cancellationToken)
    {
        Asked.Add(instrumentId);
        return ValueTask.FromResult<IReadOnlyList<QualityTarget>>([.. Targets.Where(t => instrumentId is null || t.InstrumentId == instrumentId)]);
    }

    public ValueTask<IReadOnlyList<StoredBucket>> ReadStoredBucketsAsync(Guid instrumentId, QualityBounds bounds, CancellationToken cancellationToken) =>
        Failing.Contains(instrumentId)
            ? throw new InvalidOperationException("the database is down")
            : ValueTask.FromResult(Stored.GetValueOrDefault(instrumentId) ?? []);
}

/// <summary>The run and result records in memory; one run at a time, like the store.</summary>
internal sealed class FakeQualityRuns : IQualityRuns
{
    public Dictionary<Guid, QualityRun> Runs { get; } = [];

    public List<QualityResult> Results { get; } = [];

    /// <summary>How many results each save wrote.</summary>
    public List<int> SaveBatches { get; } = [];

    /// <summary>When set, saving results throws it (the database failing).</summary>
    public Exception? SaveFailure { get; set; }

    public List<(QualityRunStatus Status, int Passed, int Warned, int Failed)> Published { get; } = [];

    public int Recovered { get; set; }

    public ValueTask<bool> TryStartAsync(QualityRun run, CancellationToken cancellationToken)
    {
        if (Runs.Values.Any(r => r.IsRunning))
        {
            return ValueTask.FromResult(false);
        }

        Runs[run.Id] = run;
        return ValueTask.FromResult(true);
    }

    public ValueTask PublishAsync(QualityRun run, CancellationToken cancellationToken)
    {
        Published.Add((run.Status, run.Passed, run.Warned, run.Failed));
        return ValueTask.CompletedTask;
    }

    public ValueTask SaveResultsAsync(IReadOnlyCollection<QualityResult> results, CancellationToken cancellationToken)
    {
        if (SaveFailure is { } failure)
        {
            throw failure;
        }

        SaveBatches.Add(results.Count);
        Results.AddRange(results);
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var interrupted = Runs.Values.Where(r => r.IsRunning && r.StartedAt < processStartedAt).ToList();
        foreach (var run in interrupted)
        {
            run.Fail(now, "interrupted");
        }

        Recovered = interrupted.Count;
        return ValueTask.FromResult(interrupted.Count);
    }
}

/// <summary>The reads of the quality results, set by the test.</summary>
internal sealed class StubQualityQueries : IQualityQueries
{
    public QualityCounts Counts { get; set; } = new(0, 0, 0, 0, 0, 0, null);

    public QualityRunView? LastFullRun { get; set; }

    /// <summary>The latest finished full run; follows <see cref="LastFullRun"/> unless it is still running (then <see cref="PreviousFinishedRun"/>).</summary>
    public QualityRunView? PreviousFinishedRun { get; set; }

    public List<QualityRunView> Runs { get; } = [];

    public StoredQualityResult? Found { get; set; }

    public QualityInstrumentSearch? LastSearch { get; private set; }

    public int? LastTake { get; private set; }

    public ValueTask<QualityCounts> GetCountsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Counts);

    public ValueTask<QualityRunView?> GetLastRunAsync(bool allInstruments, CancellationToken cancellationToken) => ValueTask.FromResult(LastFullRun);

    public ValueTask<QualityRunView?> GetLastFinishedFullRunAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(LastFullRun is { Status: not "running" } finished ? finished : PreviousFinishedRun);

    public ValueTask<IReadOnlyList<QualityRunView>> ListRunsAsync(int take, CancellationToken cancellationToken)
    {
        LastTake = take;
        return ValueTask.FromResult<IReadOnlyList<QualityRunView>>([.. Runs.Take(take)]);
    }

    public ValueTask<QualityInstrumentPage> ListInstrumentsAsync(QualityInstrumentSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        return ValueTask.FromResult(new QualityInstrumentPage([], search.Page, search.PageSize, 0));
    }

    public ValueTask<StoredQualityResult?> FindAsync(Guid instrumentId, CancellationToken cancellationToken) => ValueTask.FromResult(Found is { } f && f.InstrumentId == instrumentId ? f : null);
}
