using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.SimAll;

/// <summary>An in-memory store with the semantics of the SQL one: one active job, claims only while running, running chunks go back to pending on a redo, a final job stays final.</summary>
internal sealed class FakeSimAllStore : ISimAllStore
{
    private readonly object _gate = new();

    public SimAllJob? Job { get; set; }

    public List<Row> Chunks { get; } = [];

    public sealed class Row(NewSimAllChunk chunk)
    {
        public NewSimAllChunk Chunk { get; } = chunk;

        public string State { get; set; } = SimAllStates.Pending;

        public SimAllChunkResult? Result { get; set; }
    }

    public int Inserts { get; private set; }

    public void Seed(int strategies, int instruments, string state = SimAllStates.Running, string? pause = null)
    {
        var now = new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);
        Job = new SimAllJob(Guid.NewGuid(), state, pause, new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1), 2, "owner:me@example.com", now, now, null);
        var n = 0;
        for (var s = 1; s <= strategies; s++)
        {
            for (var i = 0; i < instruments; i++)
            {
                Chunks.Add(new Row(new NewSimAllChunk("S" + s, 1, new string((char)('a' + s), 64), new Guid(i + 1, 0, 0, new byte[8]), ++n)));
            }
        }
    }

    public ValueTask<SimAllInsert> InsertAsync(NewSimAllJob job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is { State: SimAllStates.Running or SimAllStates.Paused })
            {
                return ValueTask.FromResult(new SimAllInsert(false, Job));
            }

            Inserts++;
            Job = job.Job;
            Chunks.Clear();
            Chunks.AddRange(job.Chunks.Select(c => new Row(c)));
            return ValueTask.FromResult(new SimAllInsert(true, job.Job));
        }
    }

    public ValueTask<SimAllSnapshot?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is null)
            {
                return ValueTask.FromResult<SimAllSnapshot?>(null);
            }

            var done = Chunks.Where(c => c.State == SimAllStates.Done).ToList();
            return ValueTask.FromResult<SimAllSnapshot?>(new SimAllSnapshot(Job, new SimAllCounts(Chunks.Count, done.Count, Count(SimAllStates.Failed), Count(SimAllStates.ChunkRunning), Count(SimAllStates.Pending), done.Sum(c => c.Result!.Millis), done.Sum(c => c.Result!.RecordBytes), done.Sum(c => (long)c.Result!.Trades))));
        }
    }

    public int Count(string state) => Chunks.Count(c => c.State == state);

    public ValueTask<SimAllJob?> GetActiveAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Job is { State: SimAllStates.Running or SimAllStates.Paused } ? Job : null);
        }
    }

    public ValueTask<bool> PauseAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is { } j && j.Id == id && (j.State == SimAllStates.Running || (j.State == SimAllStates.Paused && j.PauseReason != SimAllPause.Owner && reason == SimAllPause.Owner)))
            {
                Job = j with { State = SimAllStates.Paused, PauseReason = reason, UpdatedAt = now };
                return ValueTask.FromResult(true);
            }

            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<bool> ResumeAsync(Guid id, string? onlyReason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is { } j && j.Id == id && j.State == SimAllStates.Paused && (onlyReason is null || j.PauseReason == onlyReason))
            {
                Job = j with { State = SimAllStates.Running, PauseReason = null, UpdatedAt = now };
                return ValueTask.FromResult(true);
            }

            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is { } j && j.Id == id && j.State is SimAllStates.Running or SimAllStates.Paused)
            {
                Job = j with { State = SimAllStates.Cancelled, PauseReason = null, UpdatedAt = now, FinishedAt = now };
                return ValueTask.FromResult(true);
            }

            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<int> RedoRunningChunksAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var running = Chunks.Where(c => c.State == SimAllStates.ChunkRunning).ToList();
            running.ForEach(c => c.State = SimAllStates.Pending);
            return ValueTask.FromResult(running.Count);
        }
    }

    public ValueTask<SimAllChunk?> ClaimChunkAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is not { State: SimAllStates.Running } j || j.Id != jobId || Chunks.OrderBy(c => c.Chunk.Ordinal).FirstOrDefault(c => c.State == SimAllStates.Pending) is not { } row)
            {
                return ValueTask.FromResult<SimAllChunk?>(null);
            }

            row.State = SimAllStates.ChunkRunning;
            return ValueTask.FromResult<SimAllChunk?>(new SimAllChunk(jobId, row.Chunk.StrategyId, row.Chunk.Version, row.Chunk.StrategyHash, row.Chunk.InstrumentId));
        }
    }

    public ValueTask FinishChunkAsync(SimAllChunk chunk, SimAllChunkResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var row = Chunks.Single(c => c.Chunk.StrategyId == chunk.StrategyId && c.Chunk.InstrumentId == chunk.InstrumentId);
            row.State = result.State;
            row.Result = result;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<IReadOnlyList<SimAllSweep>> ListSweepsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<SimAllSweep>>([.. Chunks.Select(c => new SimAllSweep(c.Chunk.StrategyId, c.Chunk.Version, c.Chunk.StrategyHash)).Distinct()]);
        }
    }

    public ValueTask<string?> CompleteIfFinishedAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Job is not { State: SimAllStates.Running } j || Chunks.Any(c => c.State is SimAllStates.Pending or SimAllStates.ChunkRunning))
            {
                return ValueTask.FromResult<string?>(null);
            }

            var end = SimAllStates.EndState(Count(SimAllStates.Failed));
            Job = j with { State = end, UpdatedAt = now, FinishedAt = now };
            return ValueTask.FromResult<string?>(end);
        }
    }
}

internal sealed class FakeLedger : ITestLedger
{
    public List<LedgerRow> Rows { get; } = [];

    public ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
    {
        lock (Rows)
        {
            if (Rows.Any(r => r.RunId == row.RunId))
            {
                return ValueTask.FromResult(false);
            }

            Rows.Add(row);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken) => ValueTask.FromResult(0);

    public ValueTask<TrialCounts> CountTrialsAsync(string strategyId, CancellationToken cancellationToken) => ValueTask.FromResult(new TrialCounts(0, 0, 0, 0, 0, 0, 0));

    public ValueTask<bool> HasFullRunAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<LedgerRow>>(Rows);
}

internal sealed class FakeGate : ISimAllBackfillGate
{
    public string? Incomplete { get; set; }

    public bool DataJob { get; set; }

    public ValueTask<string?> IncompleteReasonAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Incomplete);

    public ValueTask<bool> DataJobRunningAsync(CancellationToken cancellationToken) => ValueTask.FromResult(DataJob);
}

internal sealed class FakeDisk : IFreeDiskProbe
{
    public long? Free { get; set; } = 500L * SimAllSettings.Gb;

    public long? FreeBytes() => Free;
}

/// <summary>Chunks that end as told; counts how many run at once. The first <c>BarrierSize</c> chunks wait until that many are in flight, which shows the limit is reached and held.</summary>
internal sealed class FakeExecutor : ISimAllChunkExecutor
{
    private readonly TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _current;
    private int _arrived;

    public int BarrierSize { get; set; }

    public int Peak { get; private set; }

    public List<(string Strategy, Guid Instrument)> Ran { get; } = [];

    public Func<SimAllChunk, SimAllChunkResult?>? Script { get; set; }

    public Action<SimAllChunk>? OnRun { get; set; }

    public async ValueTask<SimAllChunkResult> ExecuteAsync(SimAllJob job, SimAllChunk chunk, CancellationToken hostStop)
    {
        var now = Interlocked.Increment(ref _current);
        lock (Ran)
        {
            Peak = Math.Max(Peak, now);
            Ran.Add((chunk.StrategyId, chunk.InstrumentId));
        }

        OnRun?.Invoke(chunk);
        if (BarrierSize > 0 && Interlocked.Increment(ref _arrived) <= BarrierSize)
        {
            if (_arrived == BarrierSize)
            {
                _barrier.TrySetResult();
            }

            await _barrier.Task;
        }

        Interlocked.Decrement(ref _current);
        return Script?.Invoke(chunk) ?? new SimAllChunkResult(SimAllStates.Done, new string('c', 64), 3, 1000, 10, null, null);
    }
}

/// <summary>A scope factory over a fixed set of instances (the test project has no container package).</summary>
internal sealed class FakeScopes(params (Type Type, object Instance)[] services) : IServiceScopeFactory, IServiceProvider, IServiceScope
{
    public IServiceProvider ServiceProvider => this;

    public IServiceScope CreateScope() => this;

    public object? GetService(Type serviceType) => services.FirstOrDefault(s => s.Type == serviceType).Instance;

    public void Dispose()
    {
    }
}
