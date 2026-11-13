using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.OptionChain;
using XDrishti.Application.Tests.Platform.Services;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>A Dhan the test scripts: by default every expiry list answers four Tuesdays and Octobers and every chain a ladder of 21 strikes. Records every request with the spacing it asked for and the clock at that moment.</summary>
internal sealed class FakeChainClient(SettableClock clock) : IOptionChainClient
{
    public List<(ChainRequest Request, TimeSpan Spacing, DateTimeOffset At)> Calls { get; } = [];

    /// <summary>Overrides the answer of a request; null leaves the default.</summary>
    public Func<ChainRequest, int, ChainReply?>? Responder { get; set; }

    /// <summary>How long a chain request takes in fake time.</summary>
    public TimeSpan Latency { get; set; }

    public int Activity => Calls.Count;

    public ValueTask<ChainReply> SendAsync(ChainRequest request, TimeSpan spacing, CancellationToken cancellationToken)
    {
        Calls.Add((request, spacing, clock.GetUtcNow()));
        var reply = Responder?.Invoke(request, Calls.Count) ?? Default(request);
        if (Latency > TimeSpan.Zero)
        {
            clock.Advance(Latency);
        }

        return ValueTask.FromResult(reply);
    }

    public static ChainReply Ok(byte[] body) => new(ChainOutcomes.Ok, 200, null, body, 120);

    public static ChainReply Failed(string outcome, int? status = null, string? code = null, bool noCredentials = false) => new(outcome, status, code, null, 80, noCredentials);

    private static ChainReply Default(ChainRequest request) =>
        request.Kind == ChainKind.Expiries
            ? Ok(ChainSamples.Expiries("2026-10-13", "2026-10-20", "2026-10-27", "2026-11-03", "2026-11-24", "2026-12-29"))
            : Ok(ChainSamples.Ladder("25020", 21));
}

internal sealed class FakeChainStore : IOptionChainStore
{
    private readonly Lock _gate = new();

    public List<ChainRequestRecord> Requests { get; } = [];

    public Dictionary<(DateTimeOffset, Guid, DateOnly, int, string), ChainSnapshotRow> Rows { get; } = [];

    public List<ChainSessionRecord> Sessions { get; } = [];

    public int FailAppends { get; set; }

    public long Stored { get; set; }

    public long? Free { get; set; } = 500L * OptionChainSettings.Gb;

    public int Activity
    {
        get
        {
            lock (_gate)
            {
                return Requests.Count;
            }
        }
    }

    public ValueTask<int> AppendAsync(ChainRequestRecord request, IReadOnlyList<ChainSnapshotRow> rows, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (FailAppends > 0)
            {
                FailAppends--;
                throw new InvalidOperationException("the database is away");
            }

            var inserted = 0;
            foreach (var row in rows)
            {
                inserted += Rows.TryAdd((row.Ts, row.UnderlyingId, row.Expiry, row.StrikePx, row.Side), row) ? 1 : 0;
            }

            Requests.Add(request with { Rows = inserted });
            return ValueTask.FromResult(inserted);
        }
    }

    public ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Stored);

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Free);

    public ValueTask<bool> RecordSessionAsync(ChainSessionRecord session, CancellationToken cancellationToken)
    {
        if (Sessions.Any(s => s.Date == session.Date))
        {
            return ValueTask.FromResult(false);
        }

        Sessions.Add(session);
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<DateOnly>>([.. Requests.Select(r => DateOnly.FromDateTime(r.Slot.ToOffset(ChainSamples.Ist).DateTime)).Distinct().Where(d => Sessions.All(s => s.Date != d)).Order()]);
}

internal sealed class FakeChainSwitchStore : IOptionChainSwitchStore
{
    public List<ChainSwitchRow> Rows { get; } = [];

    public bool Fail { get; set; }

    public ValueTask<ChainSwitchRow?> CurrentAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("the database is away") : ValueTask.FromResult(Rows.Count > 0 ? Rows[^1] : null);

    public ValueTask<IReadOnlyList<ChainSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChainSwitchRow>>([.. Enumerable.Reverse(Rows).Take(take)]);

    public ValueTask<ChainSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken)
    {
        var row = new ChainSwitchRow(Rows.Count + 1, enabled, actor, reason, DateTimeOffset.UnixEpoch);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    public void Set(bool enabled) => Rows.Add(new ChainSwitchRow(Rows.Count + 1, enabled, "owner:test@example.invalid", "test", DateTimeOffset.UnixEpoch));
}

internal sealed class FakeChainHeartbeats : IOptionChainHeartbeats
{
    public List<(string State, ChainHeartbeat Details)> Reports { get; } = [];

    public ChainStatus? Status { get; set; }

    public ValueTask ReportAsync(string state, ChainHeartbeat details, string instance, string version, CancellationToken cancellationToken)
    {
        Reports.Add((state, details));
        return ValueTask.CompletedTask;
    }

    public ValueTask<ChainStatus?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);
}

internal sealed class FakeChainTargets : IChainTargets
{
    public static readonly ChainTarget Nifty = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "NSE:NIFTY", 13, "IDX_I", true);

    public static readonly ChainTarget BankNifty = new(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), "NSE:BANKNIFTY", 25, "IDX_I", true);

    public static readonly ChainTarget Finnifty = new(Guid.Parse("00000000-0000-0000-0000-0000000000b3"), "NSE:FINNIFTY", 27, "IDX_I", true);

    public static readonly ChainTarget Reliance = new(Guid.Parse("00000000-0000-0000-0000-0000000000b4"), "NSE:RELIANCE", 2885, "NSE_EQ", false);

    public List<ChainTarget> Targets { get; } = [Nifty, BankNifty, Finnifty, Reliance];

    public int Loads { get; private set; }

    public ValueTask<IReadOnlyList<ChainTarget>> LoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        return ValueTask.FromResult<IReadOnlyList<ChainTarget>>([.. Targets]);
    }
}

/// <summary>A chain session the test controls: it runs until the test finishes it (or it is cancelled, which ends it as stopped).</summary>
internal sealed class FakeChainSession : IChainSession
{
    private TaskCompletionSource<ChainSessionResult>? _current;

    public string State { get; set; } = XDrishti.Application.Recording.RecorderStates.Recording;

    public ChainRunStats Stats { get; set; } = new(0, 0, 0, 0, 0);

    public bool FreeSpaceReadable { get; set; } = true;

    public int Starts { get; private set; }

    public ChainDay? LastDay { get; private set; }

    public Task<ChainSessionResult> RunAsync(ChainDay day, CancellationToken cancellationToken)
    {
        Starts++;
        LastDay = day;
        var current = new TaskCompletionSource<ChainSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = current;
        cancellationToken.Register(() => current.TrySetResult(new ChainSessionResult(ChainSessionEnd.Stopped, Stats)));
        return current.Task;
    }

    public void Finish(ChainSessionEnd end, ChainRunStats? stats = null) => _current!.TrySetResult(new ChainSessionResult(end, stats ?? Stats));

    public void Crash() => _current!.TrySetException(new InvalidOperationException("boom"));
}
