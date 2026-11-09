using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.LearnedSettings;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>AU-10 criteria 4, 5 and 10: the owner's decisions. A loosening, and the undo of a tightening, are refused without the fresh password and write nothing.</summary>
public sealed class LearnedDecisionHandlerTests
{
    private const string Owner = "owner:me@x.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeLearnedStore _store = new();
    private readonly FakeLearnedConfig _config = new();
    private readonly FakeAccountEvents _events = new();

    private LearnedDecisionSupport Support() => new(_store, _config, new FakeAuto(false), new LearnedSettingsOptions(), _events, _events, new FakeTimeProvider(), NullLogger<LearnedDecisionSupport>.Instance);

    [Fact]
    public async Task A_loosening_is_refused_without_a_fresh_password_and_nothing_is_written()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "3.5", "loosen", "proposed", null);

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("learned.password_required");
        _store.Decisions.ShouldBeEmpty();
        _store.Rows.Single().State.ShouldBe("proposed");
        _events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_loosening_with_the_password_is_applied_as_the_owner_and_audited()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "3.5", "loosen", "proposed", null);

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: true), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows.Single().DecidedBy.ShouldBe(Owner);
        _store.Rows.Single().State.ShouldBe("applied");
        _events.Recorded.Single().Event.ShouldBe(AccountEventType.LearnedSettingDecided);
    }

    [Fact]
    public async Task A_row_stored_as_tighten_whose_descriptor_says_loosen_still_needs_the_password()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "3.5", "tighten", "proposed", null); // a wrong stored direction must not open the door

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.Error.Code.ShouldBe("learned.password_required");
    }

    [Fact]
    public async Task A_tightening_proposal_is_approved_with_the_session_only()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "proposed", null);

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows.Single().DecidedBy.ShouldBe(Owner);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData("owner:")]
    [InlineData("system:learned-settings")]
    public async Task Only_a_signed_in_owner_decides(string actor)
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "proposed", null);

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, actor, FreshPassword: true), Ct);

        result.Error.Code.ShouldBe("learned.owner_only");
        _store.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Undoing_an_automatic_tightening_is_refused_without_the_password()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");

        var result = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.Error.Code.ShouldBe("learned.password_required");
        _store.Rows.Single().State.ShouldBe("applied");
    }

    [Fact]
    public async Task Undoing_an_automatic_tightening_with_the_password_puts_the_old_value_back_as_an_owner_loosening()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");

        var result = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(1, Owner, FreshPassword: true), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows[0].State.ShouldBe("undone");
        var undo = _store.Rows[1];
        (undo.OldValue, undo.NewValue, undo.Direction, undo.DecidedBy, undo.UndoOf).ShouldBe(("4.2", "4", "loosen", Owner, 1L));
    }

    [Fact]
    public async Task Undoing_an_owner_loosening_puts_the_tighter_value_back_without_a_password()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "3.5", "loosen", "applied", Owner);

        var result = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows[1].Direction.ShouldBe("tighten");
    }

    [Fact]
    public async Task A_candidate_that_breaks_the_whole_configuration_is_refused_on_approval()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "proposed", null);
        _config.Check = _ => ["hold-out bar below ladder bar"];

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);

        result.Error.Code.ShouldBe("learned.config_invalid");
        _store.Rows.Single().State.ShouldBe("proposed");
    }

    [Fact]
    public async Task A_stale_proposal_is_refused_when_the_value_moved_meanwhile()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "3.8", "4.2", "tighten", "proposed", null); // configured is 4, not 3.8

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: true), Ct);

        result.Error.Code.ShouldBe("learned.stale");
    }

    [Fact]
    public async Task Dismissing_is_the_safe_direction_and_needs_no_password()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "3.5", "loosen", "proposed", null);

        var result = await new DismissLearnedChangeHandler(Support()).HandleAsync(new DismissLearnedChangeCommand(1, Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows.Single().State.ShouldBe("dismissed");
    }

    [Fact]
    public async Task A_decided_change_cannot_be_approved_again()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "dismissed", Owner);

        var result = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: true), Ct);

        result.Error.Code.ShouldBe("learned.wrong_state");
    }

    [Fact]
    public async Task Of_two_proposals_for_one_key_only_the_first_decision_wins()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "proposed", null);
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.4", "tighten", "proposed", null);

        var first = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(1, Owner, FreshPassword: false), Ct);
        var second = await new ApproveLearnedChangeHandler(Support()).HandleAsync(new ApproveLearnedChangeCommand(2, Owner, FreshPassword: false), Ct);

        first.IsSuccess.ShouldBeTrue();
        second.Error.Code.ShouldBe("learned.stale");
        _store.Rows[1].State.ShouldBe("proposed");
    }

    [Fact]
    public async Task The_store_refuses_a_decision_that_lost_the_race_even_when_the_handler_read_an_old_value()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "proposed", null);
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.4", "tighten", "proposed", null);

        (await _store.ApplyAsync(1, Owner, null, Ct)).ShouldBeTrue();
        (await _store.ApplyAsync(2, Owner, null, Ct)).ShouldBeFalse("the newest applied value is 4.2 now, not none");
    }

    [Fact]
    public async Task Only_the_newest_applied_row_of_a_key_is_undone_and_an_undo_can_itself_be_undone()
    {
        _store.Seed(FakeLearnedConfig.BpsKey, "4", "4.2", "tighten", "applied", "auto");
        _store.Seed(FakeLearnedConfig.BpsKey, "4.2", "4.4", "tighten", "applied", "auto");

        var older = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(1, Owner, FreshPassword: true), Ct);
        var newest = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(2, Owner, FreshPassword: true), Ct);

        older.Error.Code.ShouldBe("learned.stale");
        newest.IsSuccess.ShouldBeTrue();
        var undo = _store.Rows[2];
        (undo.OldValue, undo.NewValue, undo.Direction).ShouldBe(("4.4", "4.2", "loosen"));
        undo.Snapshot.ShouldContain("maxTotalPercent"); // the real guardrails, not defaults made up for the undo

        // the undo is the newest applied row now; undoing it puts the tighter value back, a tightening, so the session is enough
        var again = await new UndoLearnedChangeHandler(Support()).HandleAsync(new UndoLearnedChangeCommand(3, Owner, FreshPassword: false), Ct);
        again.IsSuccess.ShouldBeTrue();
        again.Value.NewValue.ShouldBe("4.4");
    }
}
