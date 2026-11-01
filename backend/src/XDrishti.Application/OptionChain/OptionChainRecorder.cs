using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.OptionChain;
using XDrishti.Application.Depth;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// One day's option-chain recording (P14-02 criteria 3 to 6, 8): the expiry lists from 09:15, then every slot of the grid the index chains of the expiry rules, and from 14:30 the stock chains between the index slots. Every request goes through
/// <see cref="IOptionChainClient"/>, which keeps the 3-second spacing of the whole process; the spacing doubles for the rest of a slot after a rate limit. A request that failed writes one request row and <b>no snapshot row</b>; a slot that is
/// still running when the next one starts is closed, its missing requests counted <c>slot_overrun</c>; nothing is caught up. The disk guard looks before each slot. It reads no snapshot row and never deletes data.
/// </summary>
internal sealed class OptionChainRecorder(IServiceScopeFactory scopes, OptionChainSettings settings, TimeProvider clock, ILogger<OptionChainRecorder> logger) : IChainSession
{
    /// <summary>Slots in a row that met the rate limit before the recorder pauses.</summary>
    public const int RateLimitedSlotsBeforePause = 3;

    /// <summary>The pause after that.</summary>
    public static readonly TimeSpan RatePause = TimeSpan.FromMinutes(15);

    private readonly Dictionary<Guid, IReadOnlyList<DateOnly>> _lists = [];
    private DateOnly? _date;
    private List<StockWork>? _stockWork;
    private int _rateStreak;
    private ChainSessionEnd? _end;
    private volatile ChainRunStats _stats = new(0, 0, 0, 0, 0);
    private volatile string _state = ChainStates.Recording;
    private volatile bool _freeReadable = true;

    public string State => _state;

    public ChainRunStats Stats => _stats;

    public bool FreeSpaceReadable => _freeReadable;

    public async Task<ChainSessionResult> RunAsync(ChainDay day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(day);
        _stats = new ChainRunStats(0, 0, 0, 0, 0);
        _end = null;
        if (_date != day.Date)
        {
            _date = day.Date;
            _lists.Clear();
            _stockWork = null;
            _rateStreak = 0;
        }

        _state = ChainStates.Recording;
        try
        {
            var end = await RunDayAsync(day, cancellationToken);
            return new ChainSessionResult(end, _stats);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ChainSessionResult(ChainSessionEnd.Stopped, _stats);
        }
    }

    private async Task<ChainSessionEnd> RunDayAsync(ChainDay day, CancellationToken cancellationToken)
    {
        var slots = ChainSlotPlanner.Slots(day.Date, settings.IntervalMinutes);
        var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
        var targets = day.Indices.Concat(day.Stocks).ToList();
        var startedAt = clock.GetUtcNow();

        // The expiry lists, once per underlying, from 09:15 and only while the first slot is not too close.
        await WaitUntilAsync(ChainSlotPlanner.At(day.Date, ChainSlotPlanner.PrepStart), cancellationToken);
        foreach (var target in targets.Where(t => !_lists.ContainsKey(t.InstrumentId)))
        {
            if (clock.GetUtcNow() + settings.Spacing >= slots[0])
            {
                break;
            }

            await AskAsync(ChainKind.Expiries, target, null, slots[0], 1, new Tally(), cancellationToken);
            if (_end is { } early)
            {
                return early;
            }
        }

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            var slotEnd = slot + interval;
            if (clock.GetUtcNow() >= slotEnd)
            {
                // No catch-up. A slot that began after this session started and was missed (the Mac slept, the loop lagged) is a counted gap: the pairs the cached expiry lists plan for it are written as slot_overrun.
                // A slot that was over before the session began is not ours to count; with no cached list there is nothing to plan.
                if (slot >= startedAt)
                {
                    await RecordMissedSlotAsync(day, slot, cancellationToken);
                }

                continue;
            }

            await WaitUntilAsync(slot, cancellationToken);
            var tally = new Tally();
            var planned = await PlanIndexRequestsAsync(day, slot, tally, cancellationToken);
            if (_end is { } planning)
            {
                return planning;
            }

            if (await GuardDiskAsync(planned, slot, cancellationToken) is { } disk)
            {
                return disk;
            }

            _stats = _stats with { Slots = _stats.Slots + 1 };
            await RunIndexRequestsAsync(planned, slot, slotEnd, tally, cancellationToken);
            if (_end is { } indexEnd)
            {
                return indexEnd;
            }

            if (TimeOnly.FromTimeSpan(IndiaTime.ToIst(slot).TimeOfDay) >= ChainSlotPlanner.StockPassStart)
            {
                await RunStockPassAsync(day, slot, index + 1 < slots.Count ? slots[index + 1] : slotEnd, tally, cancellationToken);
                if (_end is { } stockEnd)
                {
                    return stockEnd;
                }
            }

            OptionChainLog.Slot(logger, index, tally.Requests, tally.Ok, tally.Gaps, tally.Rows);
            _rateStreak = tally.RateLimited > 0 ? _rateStreak + 1 : tally.Requests > 0 ? 0 : _rateStreak;
            if (_rateStreak >= RateLimitedSlotsBeforePause)
            {
                OptionChainLog.Paused(logger, _rateStreak, (int)RatePause.TotalMinutes);
                _rateStreak = 0;
                _state = ChainStates.RatePaused;
                return ChainSessionEnd.RatePaused;
            }
        }

        _state = ChainStates.DayDone;
        return ChainSessionEnd.Completed;
    }

    /// <summary>The (index, expiry) pairs of a slot; an index whose expiry list is still unknown is asked for it first (a failed ask leaves the index out of this slot).</summary>
    private async Task<List<(ChainTarget Target, DateOnly Expiry)>> PlanIndexRequestsAsync(ChainDay day, DateTimeOffset slot, Tally tally, CancellationToken cancellationToken)
    {
        var planned = new List<(ChainTarget, DateOnly)>();
        foreach (var target in day.Indices)
        {
            if (!_lists.ContainsKey(target.InstrumentId))
            {
                await AskAsync(ChainKind.Expiries, target, null, slot, 1, tally, cancellationToken);
                if (_end is not null)
                {
                    return planned;
                }
            }

            var rule = settings.IndexExpiries.FirstOrDefault(r => string.Equals(r.Symbol, target.Symbol, StringComparison.Ordinal));
            if (rule is not null && _lists.TryGetValue(target.InstrumentId, out var list))
            {
                planned.AddRange(ChainSlotPlanner.IndexExpiries(list, day.Date, rule).Select(e => (target, e)));
            }
        }

        return planned;
    }

    private async Task RecordMissedSlotAsync(ChainDay day, DateTimeOffset slot, CancellationToken cancellationToken)
    {
        var tally = new Tally();
        foreach (var target in day.Indices)
        {
            var rule = settings.IndexExpiries.FirstOrDefault(r => string.Equals(r.Symbol, target.Symbol, StringComparison.Ordinal));
            if (rule is not null && _lists.TryGetValue(target.InstrumentId, out var list))
            {
                foreach (var expiry in ChainSlotPlanner.IndexExpiries(list, day.Date, rule))
                {
                    await RecordGapAsync(ChainKind.Chain, target, expiry, slot, ChainOutcomes.SlotOverrun, tally, cancellationToken);
                }
            }
        }
    }

    private async Task RunIndexRequestsAsync(List<(ChainTarget Target, DateOnly Expiry)> planned, DateTimeOffset slot, DateTimeOffset slotEnd, Tally tally, CancellationToken cancellationToken)
    {
        for (var i = 0; i < planned.Count; i++)
        {
            if (clock.GetUtcNow() >= slotEnd)
            {
                foreach (var (target, expiry) in planned.Skip(i))
                {
                    await RecordGapAsync(ChainKind.Chain, target, expiry, slot, ChainOutcomes.SlotOverrun, tally, cancellationToken);
                }

                return;
            }

            await AskAsync(ChainKind.Chain, planned[i].Target, planned[i].Expiry, slot, tally.RateLimited > 0 ? 2 : 1, tally, cancellationToken);
            if (_end is not null)
            {
                return;
            }
        }
    }

    /// <summary>The stock chains, between the index slots: only while the next slot is far enough away for a request and its timeout; what is not asked by 15:25 is a <c>window_end</c> gap.</summary>
    private async Task RunStockPassAsync(ChainDay day, DateTimeOffset slot, DateTimeOffset nextSlot, Tally tally, CancellationToken cancellationToken)
    {
        _stockWork ??= [.. day.Stocks.Select(s => new StockWork(s, null))];
        var passEnd = ChainSlotPlanner.At(day.Date, ChainSlotPlanner.StockPassEnd);
        while (_stockWork.Count > 0)
        {
            var now = clock.GetUtcNow();
            var spacing = tally.RateLimited > 0 ? settings.Spacing * 2 : settings.Spacing;
            if (now >= passEnd)
            {
                foreach (var work in _stockWork)
                {
                    await RecordGapAsync(work.Expiry is null ? ChainKind.Expiries : ChainKind.Chain, work.Target, work.Expiry, slot, ChainOutcomes.WindowEnd, tally, cancellationToken);
                }

                _stockWork.Clear();
                return;
            }

            if (now + spacing + settings.Timeout >= nextSlot)
            {
                return; // the next slot is near: the rest waits for a later gap
            }

            var next = _stockWork[0];
            _stockWork.RemoveAt(0);
            if (next.Expiry is null)
            {
                if (!_lists.TryGetValue(next.Target.InstrumentId, out var list))
                {
                    await AskAsync(ChainKind.Expiries, next.Target, null, slot, tally.RateLimited > 0 ? 2 : 1, tally, cancellationToken);
                    if (_end is not null)
                    {
                        return;
                    }

                    if (!_lists.TryGetValue(next.Target.InstrumentId, out list))
                    {
                        continue; // the ask failed and is counted; the stock has no chain today
                    }
                }

                _stockWork.InsertRange(0, ChainSlotPlanner.StockExpiries(list, day.Date, settings.StockExpiries).Select(e => new StockWork(next.Target, e)));
                continue;
            }

            await AskAsync(ChainKind.Chain, next.Target, next.Expiry, slot, tally.RateLimited > 0 ? 2 : 1, tally, cancellationToken);
            if (_end is not null)
            {
                return;
            }
        }
    }

    /// <summary>The disk guard before a slot. On a stop the slot's planned requests are written as <c>disk_stop</c> gaps and the end is returned.</summary>
    private async Task<ChainSessionEnd?> GuardDiskAsync(List<(ChainTarget Target, DateOnly Expiry)> planned, DateTimeOffset slot, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOptionChainStore>();
        var stored = await store.StoredBytesAsync(cancellationToken);
        var free = await store.FreeBytesAsync(cancellationToken);
        if (free is null && _freeReadable)
        {
            OptionChainLog.FreeSpaceUnreadable(logger);
        }

        _freeReadable = free is not null;
        var verdict = DepthDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes);
        switch (verdict)
        {
            case DepthDiskVerdict.Warning:
                OptionChainLog.DiskWarning(logger, stored / (1024 * 1024), settings.MaxStoredBytes / (1024 * 1024));
                return null;
            case DepthDiskVerdict.OverBudget or DepthDiskVerdict.LowFree:
                var over = verdict == DepthDiskVerdict.OverBudget;
                OptionChainLog.DiskStop(logger, over ? DepthEndReasons.DiskBudget : DepthEndReasons.DiskFree);
                var tally = new Tally();
                foreach (var (target, expiry) in planned)
                {
                    await RecordGapAsync(ChainKind.Chain, target, expiry, slot, ChainOutcomes.DiskStop, tally, cancellationToken);
                }

                _state = over ? DepthStates.DiskBudget : DepthStates.DiskFree;
                return over ? ChainSessionEnd.DiskBudget : ChainSessionEnd.DiskFree;
            default:
                return null;
        }
    }

    /// <summary>One request: send, read, store the request row (and the snapshot rows of an ok chain), count, and note an ending outcome in <c>_end</c>.</summary>
    private async Task AskAsync(ChainKind kind, ChainTarget target, DateOnly? expiry, DateTimeOffset slot, int spacingFactor, Tally tally, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var request = new ChainRequest(kind, target.Scrip, target.Segment, expiry);
        var reply = await scope.ServiceProvider.GetRequiredService<IOptionChainClient>().SendAsync(request, settings.Spacing * spacingFactor, cancellationToken);
        var received = clock.GetUtcNow();
        var outcome = reply.Outcome;
        IReadOnlyList<ChainSnapshotRow> rows = [];
        if (outcome == ChainOutcomes.Ok)
        {
            (outcome, rows) = Read(kind, target, expiry, slot, received, reply.Body);
        }

        var inserted = 0;
        var storeFailed = false;
        var record = new ChainRequestRecord(slot, kind == ChainKind.Chain ? "chain" : "expiries", target.InstrumentId, expiry, outcome, reply.HttpStatus, reply.DhanCode, rows.Count, reply.LatencyMs, received);
        try
        {
            inserted = await scope.ServiceProvider.GetRequiredService<IOptionChainStore>().AppendAsync(record, rows, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            OptionChainLog.StoreFailed(logger, ex.GetType().Name);
            storeFailed = true;
        }

        // A request whose rows could not be stored is a gap, not an ok one.
        Count(tally, storeFailed ? ChainOutcomes.ServerError : outcome, inserted);
        OptionChainLog.Request(logger, record.Kind, outcome, reply.HttpStatus ?? 0, rows.Count, reply.LatencyMs);
        if (outcome != ChainOutcomes.Ok)
        {
            OptionChainLog.Gap(logger, record.Kind, outcome, reply.DhanCode ?? "-");
        }

        switch (outcome)
        {
            case ChainOutcomes.TokenRefused:
                OptionChainLog.TokenRefused(logger);
                _end = reply.NoCredentials ? ChainSessionEnd.NoToken : ChainSessionEnd.TokenRefused;
                break;
            case ChainOutcomes.NotSubscribed:
                OptionChainLog.TokenRefused(logger);
                _end = ChainSessionEnd.NotSubscribed;
                break;
            case ChainOutcomes.RateLimited:
                tally.RateLimited++;
                break;
            default:
                break;
        }
    }

    private (string Outcome, IReadOnlyList<ChainSnapshotRow> Rows) Read(ChainKind kind, ChainTarget target, DateOnly? expiry, DateTimeOffset slot, DateTimeOffset received, byte[]? body)
    {
        if (body is null)
        {
            return (ChainOutcomes.BadBody, []);
        }

        if (kind == ChainKind.Expiries)
        {
            if (OptionChainParser.ParseExpiries(body) is not { } dates)
            {
                return (ChainOutcomes.BadBody, []);
            }

            _lists[target.InstrumentId] = dates;
            return (dates.Count == 0 ? ChainOutcomes.NoData : ChainOutcomes.Ok, []);
        }

        var parsed = OptionChainParser.ParseChain(body, target.InstrumentId, expiry!.Value, slot, received, target.IsIndex ? settings.StrikesEachSide : settings.StockStrikesEachSide);
        return parsed.Status switch
        {
            ChainParseStatus.Ok => (ChainOutcomes.Ok, parsed.Rows),
            ChainParseStatus.NoData => (ChainOutcomes.NoData, []),
            _ => (ChainOutcomes.BadBody, []),
        };
    }

    /// <summary>A planned request that was never sent (overrun, window end, disk stop): one request row, no snapshot.</summary>
    private async Task RecordGapAsync(ChainKind kind, ChainTarget target, DateOnly? expiry, DateTimeOffset slot, string outcome, Tally tally, CancellationToken cancellationToken)
    {
        var record = new ChainRequestRecord(slot, kind == ChainKind.Chain ? "chain" : "expiries", target.InstrumentId, expiry, outcome, null, null, 0, 0, clock.GetUtcNow());
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IOptionChainStore>().AppendAsync(record, [], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            OptionChainLog.StoreFailed(logger, ex.GetType().Name);
        }

        Count(tally, outcome, 0);
        OptionChainLog.Gap(logger, record.Kind, outcome, "-");
    }

    private void Count(Tally tally, string outcome, int inserted)
    {
        tally.Requests++;
        tally.Rows += inserted;
        if (outcome == ChainOutcomes.Ok)
        {
            tally.Ok++;
        }
        else
        {
            tally.Gaps++;
        }

        // Requests that were never sent (the three planned-gap words) are gaps but not requests made.
        var sent = outcome is not (ChainOutcomes.SlotOverrun or ChainOutcomes.WindowEnd or ChainOutcomes.DiskStop);
        _stats = _stats with
        {
            Requests = _stats.Requests + (sent ? 1 : 0),
            Ok = _stats.Ok + (outcome == ChainOutcomes.Ok ? 1 : 0),
            Gaps = _stats.Gaps + (outcome == ChainOutcomes.Ok ? 0 : 1),
            Rows = _stats.Rows + inserted,
        };
    }

    private async Task WaitUntilAsync(DateTimeOffset instant, CancellationToken cancellationToken)
    {
        var wait = instant - clock.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, clock, cancellationToken);
        }
    }

    private sealed class Tally
    {
        public int Requests { get; set; }

        public int Ok { get; set; }

        public int Gaps { get; set; }

        public long Rows { get; set; }

        public int RateLimited { get; set; }
    }

    private sealed record StockWork(ChainTarget Target, DateOnly? Expiry);
}
