using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>In-memory stand-ins for the ports of AU-10, enough to run the handlers without a database.</summary>
internal sealed class FakeLearnedStore : ILearnedSettingStore
{
    private readonly List<LearnedChangeRow> _rows = [];

    public List<(long Id, string State, string By)> Decisions { get; } = [];

    /// <summary>True when the database would refuse an automatic decision (the Auto switch is not stored as true).</summary>
    public bool AutoSwitchOn { get; set; } = true;

    public IReadOnlyList<LearnedChangeRow> Rows => _rows;

    public ValueTask<IReadOnlyList<LearnedChangeRow>> ListAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LearnedChangeRow>>([.. _rows.OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<LearnedChangeRow?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(_rows.FirstOrDefault(r => r.Id == id));

    public ValueTask<IReadOnlyList<LearnedChangeRow>> CurrentAppliedAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LearnedChangeRow>>([.. _rows.Where(r => r.State == "applied").GroupBy(r => r.Key).Select(g => g.OrderBy(r => r.Id).Last())]);

    public ValueTask<IReadOnlyDictionary<string, string>> AppliedValuesAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, string>>(_rows.Where(r => r.State == "applied").GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).Last().NewValue));

    public ValueTask<int> AutoAppliedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_rows.Count(r => r.DecidedBy == "auto" && r.DecidedAt >= since));

    public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    public ValueTask<long?> InsertProposedAsync(NewLearnedChange change, CancellationToken cancellationToken)
    {
        if (_rows.Any(r => r.Key == change.Key && r.EvidenceHash == change.EvidenceHash))
        {
            return ValueTask.FromResult<long?>(null);
        }

        var id = _rows.Count + 1L;
        _rows.Add(new LearnedChangeRow(id, change.Key, change.OldValue, change.NewValue, change.Direction, change.Source, "proposed", null, null, null, null, change.SnapshotHash, change.Snapshot, change.EvidenceRef, change.EvidenceHash, change.UndoOf, Now));
        return ValueTask.FromResult<long?>(id);
    }

    public ValueTask<bool> ApplyAsync(long id, string decidedBy, string? expectedApplied, CancellationToken cancellationToken)
    {
        Decisions.Add((id, "applied", decidedBy));
        var i = _rows.FindIndex(r => r.Id == id && r.State == "proposed");
        if (i < 0 || (decidedBy == "auto" && (!AutoSwitchOn || _rows[i].Direction != "tighten")))
        {
            return ValueTask.FromResult(false);
        }

        var newest = _rows.Where(r => r.Key == _rows[i].Key && r.State == "applied").OrderBy(r => r.Id).LastOrDefault();
        if (newest?.NewValue != expectedApplied)
        {
            return ValueTask.FromResult(false);
        }

        _rows[i] = _rows[i] with { State = "applied", DecidedBy = decidedBy, DecidedAt = Now };
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> DismissAsync(long id, string decidedBy, CancellationToken cancellationToken)
    {
        Decisions.Add((id, "dismissed", decidedBy));
        var i = _rows.FindIndex(r => r.Id == id && r.State == "proposed");
        if (i < 0)
        {
            return ValueTask.FromResult(false);
        }

        _rows[i] = _rows[i] with { State = "dismissed", DecidedBy = decidedBy, DecidedAt = Now };
        return ValueTask.FromResult(true);
    }

    public ValueTask<long?> UndoAsync(long id, NewLearnedChange undo, string owner, CancellationToken cancellationToken)
    {
        var i = _rows.FindIndex(r => r.Id == id && r.State == "applied");
        if (i < 0 || _rows.Where(r => r.Key == _rows[i].Key && r.State == "applied").Max(r => r.Id) != id)
        {
            return ValueTask.FromResult<long?>(null);
        }

        _rows[i] = _rows[i] with { State = "undone", UndoneBy = owner, UndoneAt = Now };
        var newId = _rows.Count + 1L;
        _rows.Add(new LearnedChangeRow(newId, undo.Key, undo.OldValue, undo.NewValue, undo.Direction, undo.Source, "applied", owner, Now, null, null, undo.SnapshotHash, undo.Snapshot, undo.EvidenceRef, undo.EvidenceHash, undo.UndoOf, Now));
        return ValueTask.FromResult<long?>(newId);
    }

    public void Seed(string key, string oldValue, string newValue, string direction, string state, string? decidedBy) =>
        _rows.Add(new LearnedChangeRow(_rows.Count + 1L, key, oldValue, newValue, direction, "learning", state, decidedBy, decidedBy is null ? null : Now, null, null, new string('a', 64), "{}", "seed", "seed" + _rows.Count, null, Now));
}

internal sealed class FakeLearnedConfig : ILearnedConfig
{
    public const string BpsKey = "Confidence:CostStress:TierABps";

    public const string LoopLevel = "Confidence:Loop:Level";

    public const string NoDirectionKey = "Confidence:Ladder:MinRows";

    private readonly Dictionary<string, SettingDescriptor> _descriptors = new()
    {
        [BpsKey] = new(BpsKey, "Tier A bps", SettingTier.Advanced, SettingKind.Configuration, "4", 3, 5, SettingSafer.Higher, true),
        ["Confidence:Decay:Sigmas"] = new("Confidence:Decay:Sigmas", "Decay sigmas", SettingTier.Advanced, SettingKind.Configuration, "1", 0, 1.5m, SettingSafer.Lower, true),
        [LoopLevel] = new(LoopLevel, "Loop level", SettingTier.Advanced, SettingKind.Configuration, "0.95", 0.9m, 0.99m, SettingSafer.Higher, false),
        [NoDirectionKey] = new(NoDirectionKey, "Min rows", SettingTier.Internal, SettingKind.Configuration, "50", 5, 100000, SettingSafer.None, false),
    };

    public Dictionary<string, string> Configured { get; } = new() { [BpsKey] = "4", ["Confidence:Decay:Sigmas"] = "1", [LoopLevel] = "0.95", [NoDirectionKey] = "50" };

    /// <summary>The failures the whole-configuration check reports for a candidate; empty by default.</summary>
    public Func<IReadOnlyDictionary<string, string>, IReadOnlyList<string>> Check { get; set; } = _ => [];

    public SettingDescriptor? Descriptor(string key) => _descriptors.GetValueOrDefault(key);

    public string? ConfiguredValue(string key) => Configured.GetValueOrDefault(key);

    public IReadOnlyList<string> Failures(IReadOnlyDictionary<string, string> overrides) => Check(overrides);
}

internal sealed class FakeAuto(bool on) : IAutoSwitch
{
    public ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(on);
}

internal sealed class FakeEvidence(LearnedEvidenceBatch batch) : ILearnedEvidenceSource
{
    public ValueTask<LearnedEvidenceBatch> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(batch);
}

internal sealed class FakeAccountEvents : IAccountEventLog, IAccountUnitOfWork
{
    public List<AccountEvent> Recorded { get; } = [];

    public void Record(AccountEvent accountEvent) => Recorded.Add(accountEvent);

    public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);

    public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);

    public ValueTask<XDrishti.Domain.Common.Result> RunSerializedAsync(Func<CancellationToken, ValueTask<XDrishti.Domain.Common.Result>> work, CancellationToken cancellationToken) => work(cancellationToken);
}

internal sealed class InMemoryEssentialStore : XDrishti.Application.Abstractions.SettingsCatalog.IEssentialSettingStore
{
    private readonly Dictionary<string, XDrishti.Application.Abstractions.SettingsCatalog.StoredEssential> _rows = [];

    public ValueTask<XDrishti.Application.Abstractions.SettingsCatalog.StoredEssential?> GetAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_rows.GetValueOrDefault(key));

    public ValueTask SetAsync(string key, string value, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _rows[key] = new(value, actor, now);
        return ValueTask.CompletedTask;
    }
}
