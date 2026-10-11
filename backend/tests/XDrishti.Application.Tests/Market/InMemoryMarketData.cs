using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

/// <summary>Refresh-run store and catalog in memory. The catalog records what it was asked to write and answers with a configured result.</summary>
internal sealed class InMemoryMarketData : IRefreshRunStore, IInstrumentCatalog, IRefreshJobChannel
{
    public List<RefreshRun> Runs { get; } = [];

    public List<(RefreshPhase Phase, long Rows)> Progress { get; } = [];

    public List<RefreshJob> Jobs { get; } = [];

    public List<(IReadOnlyList<InstrumentCandidate> Candidates, bool SectorsAvailable, DateTimeOffset Now)> Applied { get; } = [];

    public List<IReadOnlyList<BrokerIdentity>> AppliedProtected { get; } = [];

    /// <summary>The rules version the stamps were asked for.</summary>
    public List<int> StampRequests { get; } = [];

    /// <summary>Makes the n-th (1-based) save fail.</summary>
    public int? FailSaveNumber { get; set; }

    public CatalogApplied Result { get; set; } = new(0, 0, 0, 0, 0, 0);

    public Exception? ApplyFailure { get; set; }

    public int ActiveCount { get; set; }

    public int Saves { get; private set; }

    public ValueTask<bool> TryStartAsync(RefreshRun run, TimeSpan staleAfter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Runs.Any(r => r.IsRunning && r.StartedAt > now - staleAfter))
        {
            return ValueTask.FromResult(false);
        }

        Runs.Add(run);
        return ValueTask.FromResult(true);
    }

    public ValueTask<RefreshRun?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.SingleOrDefault(r => r.Id == id));

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return FailSaveNumber == Saves ? throw new InvalidOperationException("database unavailable") : ValueTask.CompletedTask;
    }

    public ValueTask ReportProgressAsync(Guid id, RefreshPhase phase, long rowsRead, CancellationToken cancellationToken)
    {
        Progress.Add((phase, rowsRead));
        return ValueTask.CompletedTask;
    }

    public ValueTask<RefreshRun?> GetRunningAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Runs.LastOrDefault(r => r.IsRunning));

    public ValueTask<RefreshRun?> GetLastFinishedAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Runs.Where(r => !r.IsRunning).OrderByDescending(r => r.StartedAt).FirstOrDefault());

    public ValueTask<(SourceStamp Dhan, SourceStamp Sector)?> GetLastStampsAsync(int rulesVersion, CancellationToken cancellationToken)
    {
        StampRequests.Add(rulesVersion);
        var last = Runs.Where(r => r.Status is RefreshStatus.Succeeded or RefreshStatus.Unchanged).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        return ValueTask.FromResult<(SourceStamp, SourceStamp)?>(last is null || last.RulesVersion != rulesVersion || !last.SectorsApplied ? null : (new SourceStamp(last.DhanETag, last.DhanLastModified), new SourceStamp(last.SectorETag, last.SectorLastModified)));
    }

    public ValueTask<IReadOnlyList<RefreshRun>> ListAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<RefreshRun>>([.. Runs.OrderByDescending(r => r.StartedAt).Take(take)]);

    public int FailInterruptedCalls { get; private set; }

    public ValueTask<int> FailInterruptedAsync(DateTimeOffset processStartedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        FailInterruptedCalls++;
        var count = 0;
        foreach (var run in Runs.Where(r => r.IsRunning && r.StartedAt < processStartedAt))
        {
            run.Fail(now, "The refresh did not finish (the application stopped or crashed while it ran).");
            count++;
        }

        return ValueTask.FromResult(count);
    }

    public ValueTask<CatalogApplied> ApplyAsync(
        IReadOnlyList<InstrumentCandidate> candidates, IReadOnlyList<BrokerIdentity> protectedIds, bool sectorsAvailable, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (ApplyFailure is not null)
        {
            throw ApplyFailure;
        }

        Applied.Add((candidates, sectorsAvailable, now));
        AppliedProtected.Add(protectedIds);
        return ValueTask.FromResult(Result);
    }

    public ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(ActiveCount);

    public void Enqueue(RefreshJob job) => Jobs.Add(job);

    public IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
