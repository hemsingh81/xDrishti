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

    private LearnedDecisionSupport Support() => new(_store, _config, _events, _events, new FakeTimeProvider(), NullLogger<LearnedDecisionSupport>.Instance);

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
}
