using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>The simulation tables in memory, with the database's compare-and-set rules (one active run, moves only from the expected phase and cursor, snapshots insert-only).</summary>
internal sealed class FakeSimDaysStore : ISimDaysStore
{
    private readonly Dictionary<Guid, SimDaysRunRow> _runs = [];

    public Dictionary<Guid, List<SimDaySnapshotRow>> Days { get; } = [];

    /// <summary>Called after each advance (a test pauses auto-run or cancels here).</summary>
    public Action<SimDaysRunRow>? OnAdvance { get; set; }

    public SimDaysRunRow Row(Guid id) => With(_runs[id]);

    /// <summary>Overwrites a run (a test that stands for a kill mid-settle puts it back to settling).</summary>
    public void Put(SimDaysRunRow row) => _runs[row.Id] = row;

    public ValueTask<bool> InsertAsync(NewSimDaysRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_runs.Values.Any(r => SimDays.IsActive(r.Phase)))
        {
            return ValueTask.FromResult(false);
        }

        _runs[run.Id] = new SimDaysRunRow(run.Id, run.InputHash, run.ParamsJson, run.SettingsJson, run.VersionsJson, run.LearningSources, run.Capital, run.Mode, run.Start, run.End, run.RequestedEnd, run.Cursor, SimDays.Idle, false, run.ReadThrough, run.HoldOutNote, null, null, 0, null, run.CreatedAt, run.CreatedAt);
        Days[run.Id] = [];
        return ValueTask.FromResult(true);
    }

    public ValueTask<SimDaysRunRow?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(_runs.TryGetValue(id, out var r) ? With(r) : null);

    public ValueTask<SimDaysRunRow?> ActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_runs.Values.Where(r => SimDays.IsActive(r.Phase)).Select(With).FirstOrDefault());

    public ValueTask<SimDaysRunRow?> LatestAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_runs.Values.OrderBy(r => r.CreatedAt).Select(With).LastOrDefault());

    public ValueTask<bool> MovePhaseAsync(Guid id, string from, string to, DateTimeOffset now, CancellationToken cancellationToken) => Move(id, r => r.Phase == from, r => r with { Phase = to });

    public ValueTask<bool> RequestSettleAsync(Guid id, bool manual, DateTimeOffset now, CancellationToken cancellationToken) => Move(id, r => r.Phase == SimDays.Idle, r => r with { Phase = SimDays.Settling, Manual = manual, Waiting = null });

    public ValueTask SetWaitingAsync(Guid id, string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Moved(id, r => SimDays.IsActive(r.Phase), r => r with { Waiting = reason });
        return ValueTask.CompletedTask;
    }

    public ValueTask SetHoldOutNoteAsync(Guid id, string? note, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Moved(id, r => SimDays.IsActive(r.Phase), r => r with { HoldOutNote = note });
        return ValueTask.CompletedTask;
    }

    public ValueTask<SimTapeDay> TapeDayAsync(Guid id, DateOnly session, CancellationToken cancellationToken)
    {
        var days = Days.GetValueOrDefault(id) ?? [];
        var day = days.FirstOrDefault(d => d.Session == session);
        var before = days.Where(d => d.Session < session).OrderBy(d => d.Session).ToList();
        var stored = before.SelectMany(d => ReplayJson.Read<List<SimDayTrade>>(d.TradesJson) ?? []).ToList();
        var held = stored.Where(t => t.ExitSession is null && !stored.Any(o => o.TradeNo == t.TradeNo && o.ExitSession is not null)).OrderBy(t => t.TradeNo).ToList();
        return ValueTask.FromResult(new SimTapeDay(day?.RecordJson, day?.TradesJson, before.LastOrDefault()?.EquityClose, ReplayJson.Canonical(held)));
    }

    public ValueTask<bool> SetAutoAsync(Guid id, bool on, DateTimeOffset now, CancellationToken cancellationToken) => Move(id, r => SimDays.IsActive(r.Phase), r => r with { Auto = on });

    public ValueTask<bool> SettleAsync(Guid id, SimDayPending pending, DateTimeOffset now, CancellationToken cancellationToken) =>
        Move(id, r => r.Phase == SimDays.Settling && r.Cursor == pending.Session, r => r with { Phase = SimDays.Settled, Pending = pending, Waiting = null });

    public ValueTask<bool> AdvanceAsync(Guid id, SimDaySnapshotRow snapshot, DateOnly? next, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var moved = Moved(id, r => r.Phase == SimDays.Settled && r.Cursor == snapshot.Session, r => r with { Phase = next is null ? SimDays.Complete : SimDays.Idle, Cursor = next ?? r.Cursor, Pending = null });
        if (moved)
        {
            Days[id].Add(snapshot);
            OnAdvance?.Invoke(Row(id));
        }

        return ValueTask.FromResult(moved);
    }

    public ValueTask<bool> ExtendAsync(Guid id, DateOnly end, DateOnly requestedEnd, DateOnly cursor, DateOnly? readThrough, string? holdOutNote, DateTimeOffset now, CancellationToken cancellationToken) =>
        Move(id, r => r.Phase == SimDays.Complete && !_runs.Values.Any(o => SimDays.IsActive(o.Phase)), r => r with { Phase = SimDays.Idle, End = end, RequestedEnd = requestedEnd > r.RequestedEnd ? requestedEnd : r.RequestedEnd, Cursor = cursor, ReadThrough = readThrough, HoldOutNote = holdOutNote });

    public ValueTask<bool> FailAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        Move(id, r => SimDays.IsActive(r.Phase), r => r with { Phase = SimDays.Failed, FailReason = reason, Auto = false, Pending = null });

    public ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        Move(id, r => r.Phase is not (SimDays.Cancelled or SimDays.Failed), r => r with { Phase = SimDays.Cancelled, Auto = false, Pending = null });

    public ValueTask<IReadOnlyList<SimDaySnapshotRow>> SnapshotsAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<SimDaySnapshotRow>>([.. Days[id].OrderBy(d => d.Session)]);

    public ValueTask<SimDaySnapshotRow?> SnapshotAsync(Guid id, DateOnly session, CancellationToken cancellationToken) => ValueTask.FromResult(Days.GetValueOrDefault(id)?.FirstOrDefault(d => d.Session == session));

    public ValueTask<IReadOnlyList<SimDaysListRow>> ListAsync(int offset, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<SimDaysListRow>>([.. _runs.Values.Select(With).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip(offset).Take(take).Select(r =>
        {
            var scope = ReplayJson.Read<XDrishti.Domain.Replay.ReplayParams>(r.ParamsJson)?.Scope;
            return new SimDaysListRow(r.Id, r.Phase, r.Auto, r.Mode, scope?.BasketIds.Count ?? 0, scope?.InstrumentIds.Count ?? 0, r.Start, r.End, r.Cursor, r.Capital, r.LastEquity, r.DaysDone, r.CreatedAt);
        })]);

    public ValueTask<IReadOnlyList<SimDayListRow>> ListDaysAsync(Guid id, bool descending, int offset, int take, CancellationToken cancellationToken)
    {
        var days = Days.GetValueOrDefault(id) ?? [];
        var ordered = descending ? days.OrderByDescending(d => d.Session) : days.OrderBy(d => d.Session);
        return ValueTask.FromResult<IReadOnlyList<SimDayListRow>>([.. ordered.Skip(offset).Take(take).Select(d => new SimDayListRow(
            d.Session, d.EquityClose, (ReplayJson.Read<List<SimDayTrade>>(d.TradesJson) ?? []).Count, (ReplayJson.Read<List<object>>(d.PicksJson) ?? []).Count,
            (ReplayJson.Read<XDrishti.Domain.Replay.ReplaySessionRecord>(d.RecordJson)?.Labels ?? []).Any(l => l.StartsWith(XDrishti.Domain.Lab.SeenHoldOut.Prefix, StringComparison.Ordinal))))]);
    }

    private SimDaysRunRow With(SimDaysRunRow r) => r with { DaysDone = Days[r.Id].Count, LastEquity = Days[r.Id].Count == 0 ? null : Days[r.Id].MaxBy(d => d.Session)!.EquityClose };

    private ValueTask<bool> Move(Guid id, Func<SimDaysRunRow, bool> when, Func<SimDaysRunRow, SimDaysRunRow> to) => ValueTask.FromResult(Moved(id, when, to));

    private bool Moved(Guid id, Func<SimDaysRunRow, bool> when, Func<SimDaysRunRow, SimDaysRunRow> to)
    {
        if (!_runs.TryGetValue(id, out var r) || !when(r))
        {
            return false;
        }

        _runs[id] = to(r);
        return true;
    }
}
