using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The lifecycle and changelog use cases (spec P6-08): illegal moves refused with a reason, every record a row, a revert a new row, the state replayed from the log.</summary>
public sealed class ChangelogHandlersTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeChangelog _log = new();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private RecordCellTransitionHandler Record() => new(_log, _time, NullLogger<RecordCellTransitionHandler>.Instance);

    private RevertChangelogHandler Revert() => new(_log, _time, NullLogger<RevertChangelogHandler>.Instance);

    private static RecordCellTransitionCommand Move(CellState to, TransitionFacts? facts = null, params string[] evidence) =>
        new(Cell, to, " why ", "test", facts ?? new TransitionFacts(true, true, true, true, 20), evidence.Length == 0 ? ["run:abc"] : evidence);

    [Fact]
    public async Task A_legal_move_is_one_row_with_who_why_and_evidence()
    {
        var result = await Record().HandleAsync(Move(CellState.Candidate), Ct);

        result.IsSuccess.ShouldBeTrue();
        var row = _log.Rows.Single();
        (row.Entry.Kind, row.Entry.FromState, row.Entry.ToState, row.Entry.Actor).ShouldBe(("transition", "research", "candidate", "test"));
        row.Entry.Reason.ShouldBe("[claimed, unverified: gates=yes owner=yes band=yes recovered=yes paperDays=20] why");
        row.Entry.EvidenceIds.ShouldBe(["run:abc"]);
        row.Entry.At.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task An_illegal_move_is_refused_with_the_legal_ones_and_nothing_is_written()
    {
        var result = await Record().HandleAsync(Move(CellState.Active), Ct);

        result.Error.Code.ShouldBe(ChangelogErrors.TransitionRefusedCode);
        result.Error.Message.ShouldContain("candidate or retired");
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unmet_gate_and_a_missing_evidence_id_are_refused()
    {
        var gates = await Record().HandleAsync(Move(CellState.Candidate, new TransitionFacts()), Ct);
        var evidence = await Record().HandleAsync(new RecordCellTransitionCommand(Cell, CellState.Candidate, "r", "test", new TransitionFacts(GatesPassed: true), []), Ct);

        gates.Error.Message.ShouldContain("gates");
        evidence.Error.Message.ShouldContain("evidence");
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bad_command_is_learning_invalid_before_any_read()
    {
        var bad = await Record().HandleAsync(new RecordCellTransitionCommand("nope", (CellState)99, " ", "", new TransitionFacts(PaperDays: -1), [new string('x', 101)]), Ct);

        bad.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        bad.Error.Fields!.Select(f => f.Field).ShouldBe(["cell", "to", "reason", "actor", "paperDays", "evidence"]);
        _log.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task No_cell_becomes_active_without_the_paper_period_and_the_owner()
    {
        await WalkToPaperAsync();

        var early = await Record().HandleAsync(Move(CellState.Active, new TransitionFacts(OwnerApproved: true, WithinBand: true, PaperDays: 19)), Ct);
        var unowned = await Record().HandleAsync(Move(CellState.Active, new TransitionFacts(WithinBand: true, PaperDays: 20)), Ct);
        var ok = await Record().HandleAsync(Move(CellState.Active, new TransitionFacts(OwnerApproved: true, WithinBand: true, PaperDays: 20)), Ct);

        early.Error.Message.ShouldContain("20 are needed");
        unowned.Error.Message.ShouldContain("owner");
        ok.IsSuccess.ShouldBeTrue();
        _log.Rows[^1].Entry.PaperDays.ShouldBe(20);
    }

    [Fact]
    public async Task A_revert_is_a_new_row_pointing_at_the_old_one_and_the_old_row_is_untouched()
    {
        await Record().HandleAsync(Move(CellState.Candidate), Ct);
        var before = _log.Rows[0];

        var result = await Revert().HandleAsync(new RevertChangelogCommand(before.Id, "wrong gates", "test"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _log.Rows.Count.ShouldBe(2);
        _log.Rows[0].ShouldBeSameAs(before);
        var revert = _log.Rows[1].Entry;
        (revert.Kind, revert.RevertsId, revert.FromState, revert.ToState).ShouldBe(("revert", before.Id, "candidate", "research"));
        (await new GetCellStateHandler(_log).HandleAsync(new GetCellStateQuery(Cell), Ct)).Value.State.ShouldBe("research");
    }

    [Fact]
    public async Task Only_the_latest_transition_can_be_reverted_and_only_once_and_a_revert_is_not_reverted()
    {
        await Record().HandleAsync(Move(CellState.Candidate), Ct);
        await Record().HandleAsync(Move(CellState.Paper), Ct);

        var stale = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[0].Id, "x", "test"), Ct);
        var ok = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[1].Id, "x", "test"), Ct);
        var twice = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[1].Id, "x", "test"), Ct);
        var ofRevert = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[2].Id, "x", "test"), Ct);
        var missing = await Revert().HandleAsync(new RevertChangelogCommand(99, "x", "test"), Ct);

        stale.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        ok.IsSuccess.ShouldBeTrue();
        twice.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        ofRevert.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        missing.Error.Code.ShouldBe(ChangelogErrors.NotFoundCode);
        _log.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_revert_cannot_make_a_cell_active_again_and_a_designation_is_not_reverted_here()
    {
        await WalkToPaperAsync();
        await Record().HandleAsync(Move(CellState.Active), Ct);
        await Record().HandleAsync(Move(CellState.Retired), Ct);
        _log.Add(new NewChangelogEntry("designation", "model", "model:7", "challenger", "champion", "r", "t", ["model:7"], null, null, null, null, _time.GetUtcNow()));

        var toActive = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[^2].Id, "x", "test"), Ct);
        var designation = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[^1].Id, "x", "test"), Ct);

        toActive.Error.Message.ShouldContain("active");
        designation.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
    }

    [Fact]
    public async Task A_retirement_and_a_pause_cannot_be_reverted()
    {
        await Record().HandleAsync(Move(CellState.Retired), Ct);
        var retired = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[^1].Id, "x", "test"), Ct);

        retired.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        retired.Error.Message.ShouldContain("final");
        _log.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_mistaken_pause_cannot_be_reverted()
    {
        await WalkToPaperAsync();
        await Record().HandleAsync(Move(CellState.Active), Ct);
        await Record().HandleAsync(Move(CellState.Paused), Ct);

        var result = await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[^1].Id, "x", "test"), Ct);

        result.Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        result.Error.Message.ShouldContain("active");
    }

    [Fact]
    public async Task A_move_that_lost_a_race_is_stale_and_writes_nothing()
    {
        _log.RaceOnNextAppend = true;

        var result = await Record().HandleAsync(Move(CellState.Candidate), Ct);

        result.Error.Code.ShouldBe(ChangelogErrors.StaleCode);
        _log.Rows.Count.ShouldBe(1, "only the row that won the race");
    }

    [Fact]
    public async Task A_log_that_does_not_add_up_is_not_built_on()
    {
        _log.Add(new NewChangelogEntry("transition", "cell", Cell, "paper", "active", "r", "t", ["e"], null, null, null, null, _time.GetUtcNow()));

        var result = await Record().HandleAsync(Move(CellState.Paused), Ct);

        result.Error.Code.ShouldBe(ChangelogErrors.InconsistentCode);
    }

    [Fact]
    public async Task Replaying_the_changelog_reconstructs_the_state_the_moves_led_to_and_the_legal_next_moves()
    {
        await WalkToPaperAsync();
        await Revert().HandleAsync(new RevertChangelogCommand(_log.Rows[^1].Id, "back", "test"), Ct);
        await Record().HandleAsync(Move(CellState.Paper), Ct);

        var view = (await new GetCellStateHandler(_log).HandleAsync(new GetCellStateQuery(Cell), Ct)).Value;

        view.State.ShouldBe("paper");
        view.LegalNext.ShouldBe(["active"]);
        view.Rows.ShouldBe(4);
        view.Problems.ShouldBeEmpty();
        view.PaperDaysBeforeActive.ShouldBe(20);
        (await new GetCellStateHandler(_log).HandleAsync(new GetCellStateQuery("ORB|1m|BUY|swing|X|*"), Ct)).Value.State.ShouldBe("research");
        (await new GetCellStateHandler(_log).HandleAsync(new GetCellStateQuery("bad"), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
    }

    [Fact]
    public async Task The_read_handlers_list_get_and_validate()
    {
        await Record().HandleAsync(Move(CellState.Candidate), Ct);

        var list = await new ListChangelogHandler(_log).HandleAsync(new ListChangelogQuery("cell", null, 10), Ct);
        var one = await new GetChangelogEntryHandler(_log).HandleAsync(new GetChangelogEntryQuery(_log.Rows[0].Id), Ct);
        var missing = await new GetChangelogEntryHandler(_log).HandleAsync(new GetChangelogEntryQuery(9), Ct);
        var bad = await new ListChangelogHandler(_log).HandleAsync(new ListChangelogQuery("flower", null, 500), Ct);

        list.Value.Single().ToState.ShouldBe("candidate");
        one.Value.Subject.ShouldBe(Cell);
        missing.Error.Code.ShouldBe(ChangelogErrors.NotFoundCode);
        bad.Error.Fields!.Count.ShouldBe(2);
    }

    private async Task WalkToPaperAsync()
    {
        (await Record().HandleAsync(Move(CellState.Candidate), Ct)).IsSuccess.ShouldBeTrue();
        (await Record().HandleAsync(Move(CellState.Paper), Ct)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>An in-memory changelog with the same append rules as the table: insert only, state and latest-row checks.</summary>
    private sealed class FakeChangelog : ILearningChangelog
    {
        private readonly List<ChangelogEntry> _rows = [];

        public List<ChangelogEntry> Rows => _rows;

        public int Reads { get; private set; }

        public bool RaceOnNextAppend { get; set; }

        public void Add(NewChangelogEntry entry) => _rows.Add(new ChangelogEntry(_rows.Count + 1, entry));

        public ValueTask<ChangelogAppend> AppendAsync(NewChangelogEntry entry, CancellationToken cancellationToken)
        {
            if (RaceOnNextAppend)
            {
                RaceOnNextAppend = false;
                Add(new NewChangelogEntry("transition", "cell", entry.Subject, "research", "retired", "winner", "other", ["e"], null, null, null, null, entry.At));
            }

            var latest = _rows.LastOrDefault(r => r.Entry.Subject == entry.Subject);
            var state = latest?.Entry.ToState ?? "research";
            if (state != entry.FromState || (entry.RevertsId is { } target && latest?.Id != target))
            {
                return ValueTask.FromResult(new ChangelogAppend(0, true));
            }

            Add(entry);
            return ValueTask.FromResult(new ChangelogAppend(_rows.Count, false));
        }

        public ValueTask<ChangelogEntry?> FindAsync(long id, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult(_rows.FirstOrDefault(r => r.Id == id));
        }

        public ValueTask<IReadOnlyList<ChangelogEntry>> ListAsync(string? subjectKind, string? subject, int limit, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([.. _rows.Where(r => (subjectKind is null || r.Entry.SubjectKind == subjectKind) && (subject is null || r.Entry.Subject == subject)).Reverse().Take(limit)]);
        }

        public ValueTask<IReadOnlyList<ChangelogEntry>> HistoryAsync(string subject, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([.. _rows.Where(r => r.Entry.Subject == subject)]);
        }
    }
}
