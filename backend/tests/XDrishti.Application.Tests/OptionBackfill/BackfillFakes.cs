using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using XDrishti.Application.Tests.Platform.Services;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>A Dhan the test scripts: by default every request answers three good bars on the requested side. Records every request with the spacing it asked for and the clock at that moment.</summary>
internal sealed class FakeBackfillClient(SettableClock clock) : IOptionBackfillClient
{
    public List<(RollingRequest Request, TimeSpan Spacing, DateTimeOffset At)> Calls { get; } = [];

    /// <summary>Overrides the answer of a request (the request and the 1-based call number); null leaves the default.</summary>
    public Func<RollingRequest, int, BackfillReply?>? Responder { get; set; }

    public ValueTask<BackfillReply> SendAsync(RollingRequest request, TimeSpan spacing, CancellationToken cancellationToken)
    {
        Calls.Add((request, spacing, clock.GetUtcNow()));
        return ValueTask.FromResult(Responder?.Invoke(request, Calls.Count) ?? Ok(BackfillSamples.Body(request.Side, BackfillSamples.TimesIn(request))));
    }

    public static BackfillReply Ok(byte[] body) => new(BackfillOutcomes.Ok, 200, null, body, 120);

    public static BackfillReply Failed(string outcome, int? status = null, string? code = null, bool noCredentials = false) => new(outcome, status, code, null, 80, noCredentials);
}

/// <summary>The bar table and the fetch log in memory: the log answers the resume question exactly as the database would.</summary>
internal sealed class FakeBackfillStore : IRollingBarStore, IBackfillFetchLog
{
    private readonly Lock _gate = new();

    public List<BackfillFetchRecord> Log { get; } = [];

    public Dictionary<(DateTimeOffset, Guid, char, int, int, string, int), BackfillBar> Bars { get; } = [];

    public int FailAppends { get; set; }

    /// <summary>When set, an append of a record it matches throws (the database refuses that one answer).</summary>
    public Func<BackfillFetchRecord, bool>? FailWhen { get; set; }

    public long Stored { get; set; }

    public long? Free { get; set; } = 500L * OptionBackfillSettings.Gb;

    public ValueTask<int> AppendAsync(BackfillFetchRecord attempt, IReadOnlyList<BackfillBar> bars, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (FailWhen?.Invoke(attempt) == true)
            {
                throw new InvalidOperationException("the database refuses this answer");
            }

            if (FailAppends > 0)
            {
                FailAppends--;
                throw new InvalidOperationException("the database is away");
            }

            var inserted = 0;
            foreach (var bar in bars)
            {
                inserted += Bars.TryAdd((bar.Ts, bar.UnderlyingId, bar.ExpiryFlag, bar.ExpiryCode, bar.Offset, bar.Side, bar.IntervalMinutes), bar) ? 1 : 0;
            }

            Log.Add(attempt);
            return ValueTask.FromResult(inserted);
        }
    }

    public ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Stored);

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Free);

    public ValueTask<IReadOnlyDictionary<BackfillKey, KeyState>> LoadStateAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyDictionary<BackfillKey, KeyState> state = Log.GroupBy(r => r.Key).ToDictionary(
                g => g.Key,
                g => new KeyState(g.Count(), g.Count(r => BackfillOutcomes.IsFailedAttempt(r.Outcome)), g.Select(r => r.Outcome).Where(BackfillOutcomes.IsDone).Order(StringComparer.Ordinal).LastOrDefault(), g.Count(r => r.Outcome == BackfillOutcomes.StoreFailed)));
            return ValueTask.FromResult(state);
        }
    }

    public ValueTask<int> CountSentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Log.Count(r => r.StartedAt >= since && r.Outcome != BackfillOutcomes.DiskStop));
        }
    }
}

internal sealed class FakeBackfillSwitchStore : IOptionBackfillSwitchStore
{
    public List<BackfillSwitchRow> Rows { get; } = [];

    public bool Fail { get; set; }

    public ValueTask<BackfillSwitchRow?> CurrentAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult(Rows.Count > 0 ? Rows[^1] : null);

    public ValueTask<IReadOnlyList<BackfillSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<BackfillSwitchRow>>([.. Enumerable.Reverse(Rows).Take(take)]);

    public ValueTask<BackfillSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        var row = new BackfillSwitchRow(Rows.Count + 1, enabled, actor, reason, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    public void Set(bool enabled) => Rows.Add(new BackfillSwitchRow(Rows.Count + 1, enabled, "owner:test@example.invalid", "test", DateTimeOffset.UnixEpoch));
}

internal sealed class FakeProbeAckStore : IBackfillProbeAckStore
{
    public List<BackfillProbeAck> Rows { get; } = [];

    public bool Fail { get; set; }

    public ValueTask<BackfillProbeAck?> CurrentAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult(Rows.Count > 0 ? Rows[^1] : null);

    public ValueTask<BackfillProbeAck> AppendAsync(string actor, string note, CancellationToken cancellationToken)
    {
        var row = new BackfillProbeAck(Rows.Count + 1, actor, note, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    public void Accept() => Rows.Add(new BackfillProbeAck(Rows.Count + 1, "owner:test@example.invalid", "read the ADR table", DateTimeOffset.UnixEpoch));
}

internal sealed class FakeBackfillHeartbeats : IOptionBackfillHeartbeats
{
    public List<(string State, BackfillHeartbeat Details)> Reports { get; } = [];

    public BackfillStatus? Status { get; set; }

    public ValueTask ReportAsync(string state, BackfillHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        Reports.Add((state, details));
        return ValueTask.CompletedTask;
    }

    public ValueTask<BackfillStatus?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

internal sealed class FakeBackfillTargets : IBackfillTargets
{
    public List<BackfillTarget> Targets { get; } = [BackfillSamples.Nifty, BackfillSamples.BankNifty, BackfillSamples.Stock];

    public int Loads { get; private set; }

    public int Freezes { get; private set; }

    public ValueTask FreezeStocksAsync(CancellationToken cancellationToken)
    {
        Freezes++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<BackfillTarget>> LoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        return ValueTask.FromResult<IReadOnlyList<BackfillTarget>>([.. Targets]);
    }
}

/// <summary>A backfill session the test controls: it runs until the test finishes it (or it is cancelled, which ends it as stopped).</summary>
internal sealed class FakeBackfillSession : IBackfillSession
{
    private TaskCompletionSource<BackfillSessionResult>? _current;

    public string State { get; set; } = BackfillStates.Backfilling;

    public BackfillRunStats Stats { get; set; } = new(0, 0, 0, 0, 0, 0, 0);

    public bool FreeSpaceReadable { get; set; } = true;

    public int Starts { get; private set; }

    public IReadOnlyList<BackfillTarget>? LastTargets { get; private set; }

    public Task<BackfillSessionResult> RunAsync(IReadOnlyList<BackfillTarget> targets, CancellationToken cancellationToken)
    {
        Starts++;
        LastTargets = targets;
        var current = new TaskCompletionSource<BackfillSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = current;
        cancellationToken.Register(() => current.TrySetResult(new BackfillSessionResult(BackfillSessionEnd.Stopped, Stats)));
        return current.Task;
    }

    public void Finish(BackfillSessionEnd end, BackfillRunStats? stats = null) => _current!.TrySetResult(new BackfillSessionResult(end, stats ?? Stats));
}
