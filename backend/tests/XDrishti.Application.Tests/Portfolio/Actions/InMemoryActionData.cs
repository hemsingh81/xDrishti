using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>
/// The stores of the Action center and the price alerts in memory, with the semantics that matter for the use cases: decisions and new alerts are collected until the unit of work saves them (together with their audit events), a conditional
/// trigger or cancel executes at once inside the work of <see cref="RunAsync"/>, and a failed or throwing <see cref="RunAsync"/> puts everything back as it was. Counts the reads.
/// </summary>
internal sealed class InMemoryActionData : IActionStates, IActionSettings, IPriceAlerts, IPriceAlertStore, IPortfolioUnitOfWork, IAccountEventLog
{
    private readonly List<ActionState> _states = [];
    private readonly List<Action> _pending = [];
    private readonly List<AccountEvent> _pendingEvents = [];
    private readonly Dictionary<Guid, AlertRecord> _alerts = [];
    private readonly Dictionary<Guid, PriceAlert> _pendingAlerts = [];
    private ActionThresholdSetting _setting = new(ActionThresholds.Defaults, null, null);
    private ActionThresholds? _pendingSetting;

    // ---- what a test sets up and looks at ----

    public List<AccountEvent> Events { get; } = [];

    public Dictionary<Guid, AlertInstrument> Instruments { get; } = [];

    public Dictionary<Guid, KnownPrice> KnownPrices { get; } = [];

    /// <summary>Dhan's security id per instrument where a test needs a particular one (the others get <see cref="SecurityIdOf"/>).</summary>
    public Dictionary<Guid, string> SecurityIds { get; } = [];

    /// <summary>The portfolio cache that saves and clears drop, like the real units of work do.</summary>
    public IPortfolioHistoryCache? Cache { get; set; }

    /// <summary>When set, reading the thresholds (the first read of an evaluation) fails like a database error.</summary>
    public bool ThrowOnSettingsRead { get; set; }

    /// <summary>Runs when a save is refused, to play the concurrent request whose change caused the collision.</summary>
    public Action? OnRefusedSave { get; set; }

    /// <summary>A decision as if another request had stored it (no audit event).</summary>
    public void SeedState(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, DateTimeOffset at) =>
        _states.Add(ActionState.Create(rule, key, state, snoozedUntil, "other@example.com", at));

    /// <summary>When set, clearing decisions fails like a database error.</summary>
    public bool ThrowOnClear { get; set; }

    public int StateReads { get; private set; }

    public int Saves { get; private set; }

    public bool RefuseNextSave { get; set; }

    public ActionThresholds Thresholds => _setting.Values;

    public IReadOnlyList<ActionStateRow> AllStates => [.. _states.Select(Row)];

    public ActionState? StateOf(string rule, string key) => _states.FirstOrDefault(s => s.Rule == rule && s.Key == key);

    public IReadOnlyList<AlertRecord> Alerts => [.. _alerts.Values.OrderBy(a => a.Alert.CreatedAt)];

    public AlertRecord AlertOf(Guid id) => _alerts[id];

    /// <summary>An active alert as if created earlier (no audit event).</summary>
    public PriceAlert SeedAlert(Guid instrumentId, AlertDirection direction, decimal level, DateTimeOffset createdAt, string? note = null)
    {
        var alert = PriceAlert.Create(instrumentId, direction, level, note, "owner@example.com", createdAt);
        _alerts[alert.Id] = new AlertRecord(alert, AlertStatus.Active, null, null);
        return alert;
    }

    public AlertInstrument AddInstrument(string symbol, string type = "share", string exchange = "NSE", bool retired = false, bool quotable = true)
    {
        var instrument = new AlertInstrument(ActionFixtures.InstrumentOf(symbol), symbol, symbol + " Ltd", type, exchange, retired, quotable);
        Instruments[instrument.Id] = instrument;
        return instrument;
    }

    // ---- IActionStates ----

    public ValueTask<IReadOnlyList<ActionStateRow>> ListLiveAsync(CancellationToken cancellationToken)
    {
        StateReads++;
        return ValueTask.FromResult<IReadOnlyList<ActionStateRow>>([.. _states.Where(s => s.State != ActionDecision.Cleared).Select(Row)]);
    }

    public ValueTask<bool> SetAsync(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = _states.FirstOrDefault(s => s.Rule == rule && s.Key == key);
        if (row is null)
        {
            if (state == ActionDecision.Cleared)
            {
                return ValueTask.FromResult(false);
            }

            var created = ActionState.Create(rule, key, state, snoozedUntil, actor, now);
            _pending.Add(() => _states.Add(created));
            return ValueTask.FromResult(true);
        }

        // Mirrors the tracked entity: whether it would change is known now, the change is applied by the save.
        var probe = ActionState.Create(row.Rule, row.Key, row.State, row.SnoozedUntil, row.UpdatedBy, row.UpdatedAt);
        if (!probe.Decide(state, snoozedUntil, actor, now))
        {
            return ValueTask.FromResult(false);
        }

        _pending.Add(() => row.Decide(state, snoozedUntil, actor, now));
        return ValueTask.FromResult(true);
    }

    public ValueTask<int> ClearDoneAsync(IReadOnlyList<ActionStateRow> rows, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (ThrowOnClear)
        {
            throw new InvalidOperationException("The database is down.");
        }

        var cleared = 0;
        foreach (var row in rows)
        {
            var stored = _states.FirstOrDefault(s => s.Rule == row.Rule && s.Key == row.Key);
            if (stored is { State: ActionDecision.Done } && stored.UpdatedAt == row.UpdatedAt)
            {
                stored.Decide(ActionDecision.Cleared, null, actor, now);
                cleared++;
                Cache?.Invalidate();
            }
        }

        return ValueTask.FromResult(cleared);
    }

    // ---- IActionSettings ----

    public ValueTask<ActionThresholdSetting> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ThrowOnSettingsRead ? throw new InvalidOperationException("The database is down.") : ValueTask.FromResult(_setting);
    }

    public ValueTask SetAsync(ActionThresholds thresholds, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _pendingSetting = thresholds;
        _pending.Add(() => _setting = new ActionThresholdSetting(_pendingSetting!, actor, now));
        return ValueTask.CompletedTask;
    }

    // ---- IPriceAlerts ----

    public ValueTask<IReadOnlyList<ActiveAlert>> ListActiveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ActiveAlert>>(
        [
            .. _alerts.Values.Where(a => a.Status == AlertStatus.Active).OrderBy(a => a.Alert.CreatedAt).Select(a =>
            {
                var instrument = Instruments.GetValueOrDefault(a.Alert.InstrumentId);
                return new ActiveAlert(a.Alert.Id, a.Alert.InstrumentId, instrument?.Symbol ?? "?", instrument is { Quotable: true, Retired: false } ? SecurityIds.GetValueOrDefault(a.Alert.InstrumentId) ?? SecurityIdOf(a.Alert.InstrumentId) : null, a.Alert.Direction, a.Alert.Level);
            }),
        ]);

    /// <summary>The Dhan security id the fake master gives an instrument.</summary>
    public static string SecurityIdOf(Guid instrumentId) => "S" + instrumentId.ToString("N")[^8..];

    public ValueTask<bool> TryTriggerAsync(Guid alertId, decimal price, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!_alerts.TryGetValue(alertId, out var alert) || alert.Status != AlertStatus.Active)
        {
            return ValueTask.FromResult(false);
        }

        _alerts[alertId] = alert with { Status = AlertStatus.Triggered, TriggeredAt = at, TriggerPrice = price };
        return ValueTask.FromResult(true);
    }

    // ---- IPriceAlertStore ----

    public ValueTask<AlertInstrument?> FindInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken) => ValueTask.FromResult(Instruments.GetValueOrDefault(instrumentId));

    public ValueTask<KnownPrice?> GetKnownPriceAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(KnownPrices.TryGetValue(instrumentId, out var known) ? known : null);

    public ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_alerts.Values.Count(a => a.Status == AlertStatus.Active));

    public void Add(PriceAlert alert) => _pendingAlerts[alert.Id] = alert;

    public ValueTask<AlertRow?> FindAsync(Guid alertId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_alerts.TryGetValue(alertId, out var alert) ? RowOf(alert) : null);

    public ValueTask<(IReadOnlyList<AlertRow> Items, int Total)> ListAsync(AlertStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var all = _alerts.Values.Where(a => status is null || a.Status == status).OrderByDescending(a => a.Alert.CreatedAt).ThenByDescending(a => a.Alert.Id).ToList();
        return ValueTask.FromResult<(IReadOnlyList<AlertRow>, int)>(([.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(RowOf)], all.Count));
    }

    public ValueTask<IReadOnlyList<AlertRow>> ListTriggeredSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AlertRow>>(
            [.. _alerts.Values.Where(a => a.Status == AlertStatus.Triggered && a.TriggeredAt >= since).OrderByDescending(a => a.TriggeredAt).Select(RowOf)]);

    public ValueTask<bool> TryCancelAsync(Guid alertId, CancellationToken cancellationToken)
    {
        if (!_alerts.TryGetValue(alertId, out var alert) || alert.Status != AlertStatus.Active)
        {
            return ValueTask.FromResult(false);
        }

        _alerts[alertId] = alert with { Status = AlertStatus.Cancelled };
        return ValueTask.FromResult(true);
    }

    // ---- IPortfolioUnitOfWork ----

    public ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        if (RefuseNextSave)
        {
            RefuseNextSave = false;
            Discard();
            throw new InvalidOperationException("The database refused the save.");
        }

        Commit();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        if (RefuseNextSave)
        {
            RefuseNextSave = false;
            Discard();
            OnRefusedSave?.Invoke();
            return ValueTask.FromResult(false);
        }

        Commit();
        return ValueTask.FromResult(true);
    }

    public async ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        var states = _states.Select(s => ActionState.Create(s.Rule, s.Key, s.State, s.SnoozedUntil, s.UpdatedBy, s.UpdatedAt)).ToList();
        var alerts = new Dictionary<Guid, AlertRecord>(_alerts);
        var events = Events.Count;
        Result result;
        try
        {
            result = await work(cancellationToken);
        }
        catch
        {
            Restore(states, alerts, events);
            throw;
        }

        if (result.IsFailure)
        {
            Restore(states, alerts, events);
        }

        return result;
    }

    // ---- IAccountEventLog ----

    public void Record(AccountEvent accountEvent) => _pendingEvents.Add(accountEvent);

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AccountEvent>>([.. Events.Where(e => e.AccountId == accountId).Take(take)]);

    private static ActionStateRow Row(ActionState s) => new(s.Rule, s.Key, s.State, s.SnoozedUntil, s.UpdatedAt);

    private AlertRow RowOf(AlertRecord record)
    {
        var instrument = Instruments.GetValueOrDefault(record.Alert.InstrumentId);
        return new AlertRow(
            record.Alert.Id, record.Alert.InstrumentId, instrument?.Symbol ?? "?", instrument?.Name ?? "?", record.Alert.Direction, record.Alert.Level, record.Alert.Note,
            record.Status, record.Alert.CreatedAt, record.TriggeredAt, record.TriggerPrice);
    }

    private void Commit()
    {
        Saves++;
        foreach (var change in _pending)
        {
            change();
        }

        foreach (var added in _pendingAlerts.Values)
        {
            _alerts[added.Id] = new AlertRecord(added, AlertStatus.Active, null, null);
        }

        Events.AddRange(_pendingEvents);
        Discard();
        Cache?.Invalidate();
    }

    private void Discard()
    {
        _pending.Clear();
        _pendingAlerts.Clear();
        _pendingEvents.Clear();
        _pendingSetting = null;
    }

    private void Restore(List<ActionState> states, Dictionary<Guid, AlertRecord> alerts, int events)
    {
        Discard();
        _states.Clear();
        _states.AddRange(states);
        _alerts.Clear();
        foreach (var (id, alert) in alerts)
        {
            _alerts[id] = alert;
        }

        Events.RemoveRange(events, Events.Count - events);
    }

    /// <summary>An alert as stored: the entity as created, and the status and trigger that conditional updates changed.</summary>
    internal sealed record AlertRecord(PriceAlert Alert, AlertStatus Status, DateTimeOffset? TriggeredAt, decimal? TriggerPrice);
}
