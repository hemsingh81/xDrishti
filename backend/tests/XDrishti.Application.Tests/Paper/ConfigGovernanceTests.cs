using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Paper;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-05: the gate over fake ports, the hand-worked fixture, the owner acts and the queue. Nothing is applied by any of them.</summary>
public sealed class ConfigGovernanceTests
{
    private const string Cell20 = "ORB|15m|BUY|INTRADAY|FIXED_RR{rr=2.0}|*";

    private const string Cell25 = "ORB|15m|BUY|INTRADAY|FIXED_RR{rr=2.5}|*";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeStore _store = new();

    private readonly IPaperStore _paper = Substitute.For<IPaperStore>();

    private readonly IConfigSnapshotSource _source = Substitute.For<IConfigSnapshotSource>();

    private readonly IHypothesisLog _hypotheses = Substitute.For<IHypothesisLog>();

    private readonly ILearningChangelog _changelog = Substitute.For<ILearningChangelog>();

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    private List<ConfigComponent> _components = [new("accounts.paper/a/risk_per_trade_pct", "1.0"), new("cells.eligible/" + Cell20, "h1")];

    private List<string> _unreadable = [];

    public ConfigGovernanceTests()
    {
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _source.ReadAsync(Ct).Returns(_ => new ConfigReading(_components, _unreadable));
        _paper.CurrentEpochAsync(Ct).Returns(_ => _store.Epochs.Count == 0 ? null : _store.Epochs[^1]);
        _hypotheses.ListAsync("accepted", Arg.Any<int>(), Ct).Returns([]);
        _changelog.ListAsync("cell", null, Arg.Any<int>(), Ct).Returns([]);
    }

    private FingerprintEpochGate Gate() => new(_paper, _store, _source, _hypotheses, _changelog, _strategies, _calendar, _time, NullLogger<FingerprintEpochGate>.Instance);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private async Task<EpochResolution> RunAsync(params DateOnly[] sessions) => await Gate().ResolveAsync(sessions, Ct);

    [Fact]
    public async Task The_first_run_creates_the_first_epoch_and_a_snapshot_and_measures()
    {
        var r = await RunAsync(D(11, 2));

        r.Epoch.Kind.ShouldBe("first");
        r.MeasureStatus[D(11, 2)].ShouldBe("measured");
        _store.Snapshots.Count.ShouldBe(1);
        _store.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_hand_worked_fixture_a_risk_cut_is_noted_and_a_new_cell_starts_epoch_two_with_the_session_set_aside()
    {
        await RunAsync(D(11, 2));

        _time.SetUtcNow(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        _components = [new("accounts.paper/a/risk_per_trade_pct", "0.5"), new("cells.eligible/" + Cell20, "h1")];
        var noted = await RunAsync(D(11, 5));

        noted.MeasureStatus[D(11, 5)].ShouldBe("measured");
        _store.Epochs.Count.ShouldBe(1);
        _store.Changes.Single().ShouldBe(new NewChange("accounts.paper/a/risk_per_trade_pct", "risk_down", "1.0", "0.5", "noted", null));

        var h7 = new HypothesisRow(7, new NewHypothesisRow(null, "accepted", "judged", "owner", Cell20, "exit", "Exit", Cell20.Split('|')[4], Cell25.Split('|')[4], "why", "b", "c", 3, "ok", "owner", new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero)));
        _hypotheses.ListAsync("accepted", Arg.Any<int>(), Ct).Returns([h7]);
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        _components = [new("accounts.paper/a/risk_per_trade_pct", "0.5"), new("cells.eligible/" + Cell20, "h1"), new("cells.eligible/" + Cell25, "h3")];
        var frozen = await RunAsync(D(11, 10));

        frozen.MeasureStatus[D(11, 10)].ShouldBe("straddle");
        frozen.Epoch.Id.ShouldBe(1);
        _store.Epochs.Count.ShouldBe(2);
        _store.Epochs[1].Kind.ShouldBe("detected_change");
        _store.Epochs[1].StartedOn.ShouldBe(D(11, 11));
        _store.Changes.Last().ShouldBe(new NewChange("cells.eligible/" + Cell25, "frozen", null, "h3", "new_epoch", "hypothesis:7"));

        var again = await RunAsync(D(11, 11));
        again.MeasureStatus[D(11, 11)].ShouldBe("measured");
        _store.Epochs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_catch_up_of_three_sessions_over_a_frozen_change_sets_all_three_aside_and_the_new_epoch_starts_after_the_last()
    {
        await RunAsync(D(11, 2));
        _components = [new("planning.builder:useselection", "true"), .. _components];

        var r = await RunAsync(D(11, 3), D(11, 4), D(11, 5));

        r.MeasureStatus.Values.ShouldAllBe(v => v == "straddle");
        _store.Epochs[^1].StartedOn.ShouldBe(D(11, 6));
        _store.Changes.Single().ExplainedBy.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_with_the_same_configuration_changes_nothing()
    {
        await RunAsync(D(11, 2));
        var r = await RunAsync(D(11, 3));

        r.MeasureStatus[D(11, 3)].ShouldBe("measured");
        _store.Epochs.Count.ShouldBe(1);
        _store.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_group_claims_no_change_and_counts_only_near_the_last_full_snapshot()
    {
        await RunAsync(D(11, 2));
        _unreadable = ["accounts"];
        _components = [];

        var near = await RunAsync(D(11, 5));
        var far = await RunAsync(D(11, 9));

        near.MeasureStatus[D(11, 5)].ShouldBe("measured");
        far.MeasureStatus[D(11, 9)].ShouldBe("straddle");
        _store.Epochs.Count.ShouldBe(1);
        _store.Changes.ShouldBeEmpty();
        _store.Snapshots.Count(s => s.Partial).ShouldBe(2);
    }

    [Fact]
    public async Task With_epochs_but_no_snapshot_the_snapshot_is_taken_and_no_change_is_claimed()
    {
        _store.Epochs.Add(new EpochRow(1, D(11, 2), DateTimeOffset.UnixEpoch, "first", "x", "system"));

        var r = await RunAsync(D(11, 3));

        r.MeasureStatus[D(11, 3)].ShouldBe("measured");
        _store.Snapshots.Count.ShouldBe(1);
        _store.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Asking_for_the_epoch_without_sessions_reads_nothing_once_an_epoch_exists()
    {
        await RunAsync(D(11, 2));
        _source.ClearReceivedCalls();

        var r = await RunAsync();

        r.MeasureStatus.ShouldBeEmpty();
        await _source.DidNotReceive().ReadAsync(Ct);
    }

    private StartEpochHandler Start() => new(_paper, _store, _calendar, _time, NullLogger<StartEpochHandler>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public async Task A_restart_without_a_usable_reason_is_refused(string? reason)
    {
        await RunAsync(D(11, 2));

        var r = await Start().HandleAsync(new StartEpochCommand(reason, "cli:xd"), Ct);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(PaperErrors.InvalidCode);
        _store.Epochs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_restart_writes_an_owner_epoch_for_the_next_trading_day_and_deletes_nothing_then_is_refused_on_the_same_day()
    {
        await RunAsync(D(11, 2));
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

        var first = await Start().HandleAsync(new StartEpochCommand("pre-flight done, restart the count", "cli:xd"), Ct);
        var second = await Start().HandleAsync(new StartEpochCommand("pre-flight done, restart again", "cli:xd"), Ct);

        first.IsSuccess.ShouldBeTrue();
        _store.Epochs[^1].Kind.ShouldBe("owner_restart");
        _store.Epochs[^1].StartedOn.ShouldBe(D(11, 9));
        _store.Epochs.Count.ShouldBe(2);
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task A_restart_with_no_epoch_is_refused()
    {
        var r = await Start().HandleAsync(new StartEpochCommand("a reason that is long enough", "cli:xd"), Ct);

        r.Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task Explain_stores_a_note_for_a_known_change_and_refuses_an_unknown_one_or_hypothesis()
    {
        await RunAsync(D(11, 2));
        _components = [new("planning.builder:useselection", "true"), .. _components];
        await RunAsync(D(11, 3));
        var changeId = _store.StoredChanges.Single().Id;
        var handler = new ExplainChangeHandler(_store, _hypotheses, _time, NullLogger<ExplainChangeHandler>.Instance);
        _hypotheses.FindAsync(9, Ct).Returns(new HypothesisRow(9, null!));
        _hypotheses.FindAsync(10, Ct).Returns((HypothesisRow?)null);

        var ok = await handler.HandleAsync(new ExplainChangeCommand(changeId, 9, "I changed the builder on purpose", "cli:xd"), Ct);
        var noChange = await handler.HandleAsync(new ExplainChangeCommand(999, null, "I changed the builder on purpose", "cli:xd"), Ct);
        var noHypothesis = await handler.HandleAsync(new ExplainChangeCommand(changeId, 10, "I changed the builder on purpose", "cli:xd"), Ct);
        var shortReason = await handler.HandleAsync(new ExplainChangeCommand(changeId, null, "no", "cli:xd"), Ct);

        ok.IsSuccess.ShouldBeTrue();
        _store.Notes.Single().HypothesisId.ShouldBe(9);
        noChange.Error.Code.ShouldBe(PaperErrors.NotFoundCode);
        noHypothesis.Error.Code.ShouldBe(PaperErrors.NotFoundCode);
        shortReason.Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task The_queue_lists_accepted_hypotheses_no_change_or_note_names_and_applies_nothing()
    {
        HypothesisRow Row(long id) => new(id, new NewHypothesisRow(null, "accepted", "judged", "owner", Cell20, "exit", "Exit", "a", "b", "why", "b", "c", 3, "ok", "owner", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        _hypotheses.ListAsync("accepted", Arg.Any<int>(), Ct).Returns([Row(9), Row(7)]);
        _store.Used.Add(7);

        var queue = (await new GetQueueHandler(_hypotheses, _store, _time).HandleAsync(new GetQueueQuery(), Ct)).Value;

        queue.Items.Select(i => i.HypothesisId).ShouldBe([9]);
        queue.Items[0].AgeDays.ShouldBe(4);
        queue.Note.ShouldContain("nothing was applied");
        _hypotheses.ReceivedCalls().Select(c => c.GetMethodInfo().Name).ShouldNotContain("AppendAsync");
        _hypotheses.ReceivedCalls().Select(c => c.GetMethodInfo().Name).ShouldNotContain("ProposeAsync");
    }

    [Fact]
    public async Task The_config_view_shows_the_pending_difference_and_each_enabled_learning_writer_as_a_blocker_and_stores_nothing()
    {
        await RunAsync(D(11, 2));
        _components = [new("accounts.paper/a/risk_per_trade_pct", "0.5"), new("models.champion", "2"), .. _components.Skip(1)];
        var services = Substitute.For<IServiceScheduleReader>();
        services.GetAsync("cells", Ct).Returns(new ServiceScheduleInfo(true, null, "", null));
        services.GetAsync("retrain", Ct).Returns(new ServiceScheduleInfo(false, null, "", null));
        services.GetAsync("weekly", Ct).Returns((ServiceScheduleInfo?)null);
        var snapshots = _store.Snapshots.Count;

        var view = (await new GetConfigHandler(_source, _store, services).HandleAsync(new GetConfigQuery(), Ct)).Value;

        view.Blockers.Select(b => b.ServiceId).ShouldBe(["cells"]);
        view.Blockers[0].Text.ShouldContain("each run restarts the count");
        view.Pending.Select(p => (p.Key, p.Meaning)).ShouldBe([("accounts.paper/a/risk_per_trade_pct", "noted only"), ("models.champion", "would restart the count")]);
        view.Components.ShouldNotBeEmpty();
        _store.Snapshots.Count.ShouldBe(snapshots);
    }

    [Fact]
    public void The_pinned_learning_writers_are_cells_retrain_and_weekly()
    {
        PaperConfigKeys.LearningWriterServices.ShouldBe(["cells", "retrain", "weekly"]);
    }

    private sealed class FakeStore : IGovernanceStore
    {
        public List<EpochRow> Epochs { get; } = [];

        public List<StoredSnapshot> StoredSnapshots { get; } = [];

        public List<NewSnapshot> Snapshots { get; } = [];

        public List<NewChange> Changes { get; } = [];

        public List<StoredChange> StoredChanges { get; } = [];

        public List<EpochNote> Notes { get; } = [];

        public HashSet<long> Used { get; } = [];

        public ValueTask<StoredSnapshot?> LatestSnapshotAsync(bool fullOnly, CancellationToken cancellationToken) =>
            ValueTask.FromResult(StoredSnapshots.Where(s => !fullOnly || !s.Partial).LastOrDefault());

        public ValueTask<GovernanceCommitted> CommitAsync(GovernanceCommit commit, CancellationToken cancellationToken)
        {
            var s = commit.Snapshot;
            Snapshots.Add(s);
            var snapshot = new StoredSnapshot(StoredSnapshots.Count + 1, s.TakenAt, s.SessionDate, s.Hash, s.ComponentsJson, s.Partial);
            StoredSnapshots.Add(snapshot);
            var epochId = commit.CurrentEpochId;
            if (commit.NewEpoch is { } e)
            {
                Epochs.Add(new EpochRow(Epochs.Count + 1, e.StartedOn, e.At, "detected_change", e.Reason, "system:paper-capture"));
                epochId = Epochs[^1].Id;
            }

            foreach (var c in commit.Changes)
            {
                Changes.Add(c);
                StoredChanges.Add(new StoredChange(StoredChanges.Count + 1, commit.FromSnapshotId ?? snapshot.Id, snapshot.Id, c.Key, c.Class, c.Before, c.After, c.Effect, epochId, c.ExplainedBy));
            }

            return ValueTask.FromResult(new GovernanceCommitted(snapshot.Id, epochId));
        }

        public ValueTask<EpochRow> CreateFirstEpochAsync(DateOnly startedOn, DateTimeOffset at, string? hash, CancellationToken cancellationToken)
        {
            if (Epochs.Count == 0)
            {
                Epochs.Add(new EpochRow(1, startedOn, at, "first", "the default epoch", "system:paper-capture"));
            }

            return ValueTask.FromResult(Epochs[^1]);
        }

        public ValueTask<EpochRow?> StartOwnerEpochAsync(NewOwnerEpoch epoch, CancellationToken cancellationToken)
        {
            Epochs.Add(new EpochRow(Epochs.Count + 1, epoch.StartedOn, epoch.At, "owner_restart", epoch.Reason, epoch.Actor));
            return ValueTask.FromResult<EpochRow?>(Epochs[^1]);
        }

        public ValueTask<IReadOnlyList<EpochSummary>> ListEpochsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpochSummary>>([.. Epochs.AsEnumerable().Reverse().Select(e => new EpochSummary(e, null, 0, 0, 0, 0))]);

        public ValueTask<IReadOnlyList<StoredChange>> ListChangesAsync(long? epochId, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredChange>>([.. StoredChanges.Where(c => epochId is null || c.EpochId == epochId)]);

        public ValueTask<StoredChange?> FindChangeAsync(long id, CancellationToken cancellationToken) =>
            ValueTask.FromResult(StoredChanges.FirstOrDefault(c => c.Id == id));

        public ValueTask<IReadOnlyList<EpochNote>> ListNotesAsync(long? epochId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EpochNote>>([.. Notes]);

        public ValueTask AddNoteAsync(long epochId, long? changeId, long? hypothesisId, string text, string actor, DateTimeOffset at, CancellationToken cancellationToken)
        {
            Notes.Add(new EpochNote(Notes.Count + 1, epochId, changeId, hypothesisId, text, actor, at));
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlySet<long>> UsedHypothesisIdsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<long>>(Used);
    }
}
