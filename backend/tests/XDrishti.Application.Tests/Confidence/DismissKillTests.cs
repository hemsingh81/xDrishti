using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>CF-12 leftover (ADR 0234): the owner's dismissal of a kill proposal records the owner and the reason, changes no cell, is idempotent, and a dismissed proposal can never be applied.</summary>
public sealed class DismissKillTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Owner = "owner:hem";

    private static async Task<KillFixture> ProposedAsync()
    {
        var f = new KillFixture { ReportResult = Result.Success(KillFixture.View(KillFixture.Kill("cost_stress", "fires"))) };
        await f.WalkAsync(KillFixture.Cell, CellState.Candidate, CellState.Paper);
        await f.ProposeAsync();
        return f;
    }

    [Fact]
    public async Task Dismissing_records_the_owner_and_the_reason_and_changes_no_cell()
    {
        var f = await ProposedAsync();
        var rows = f.Log.Rows.Count;

        var result = await f.Dismiss.HandleAsync(new DismissKillCommand(1, Owner, "  seen, a data glitch  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.State, result.Value.AlreadyDismissed, result.Value.Actor, result.Value.Reason).ShouldBe(("dismissed", false, Owner, "seen, a data glitch"));
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.DismissReason, row.AutoSettingId).ShouldBe(("dismissed", Owner, "seen, a data glitch", null));
        f.Log.Rows.Count.ShouldBe(rows);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
    }

    [Fact]
    public async Task Dismissing_twice_changes_nothing_twice_and_the_first_reason_stands()
    {
        var f = await ProposedAsync();
        (await f.Dismiss.HandleAsync(new DismissKillCommand(1, Owner, "first"), Ct)).IsSuccess.ShouldBeTrue();

        var again = await f.Dismiss.HandleAsync(new DismissKillCommand(1, "owner:other", "second"), Ct);

        again.IsSuccess.ShouldBeTrue();
        (again.Value.AlreadyDismissed, again.Value.Actor, again.Value.Reason).ShouldBe((true, Owner, "first"));
        f.Store.Rows.Single().DismissReason.ShouldBe("first");
    }

    [Fact]
    public async Task A_dismissed_proposal_cannot_be_applied_even_with_the_setting_on()
    {
        var f = await ProposedAsync();
        (await f.Dismiss.HandleAsync(new DismissKillCommand(1, Owner, "no"), Ct)).IsSuccess.ShouldBeTrue();
        f.Switch = new KillSwitch(5, true);

        var applied = await f.Apply.HandleAsync(new ApplyKillCommand(1, Owner), Ct);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe(KillErrors.ProposalClosedCode);
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paper);
    }

    [Fact]
    public async Task An_applied_proposal_cannot_be_dismissed()
    {
        var f = await ProposedAsync();
        (await f.Apply.HandleAsync(new ApplyKillCommand(1, Owner), Ct)).IsSuccess.ShouldBeTrue();

        var result = await f.Dismiss.HandleAsync(new DismissKillCommand(1, Owner, "late"), Ct);

        result.Error.Code.ShouldBe(KillErrors.ProposalAppliedCode);
        f.Store.Rows.Single().State.ShouldBe("applied");
    }

    [Fact]
    public async Task A_dismissal_between_the_pauses_and_the_decision_is_not_reported_as_applied_and_names_the_paused_cells()
    {
        var f = await ProposedAsync();
        f.Store.BeforeDecide = id => f.Store.DismissAsync(id, "owner:other", "dismissed meanwhile", Ct).AsTask().GetAwaiter().GetResult();

        var applied = await f.Apply.HandleAsync(new ApplyKillCommand(1, Owner), Ct);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.Code.ShouldBe(KillErrors.DismissedDuringApplyCode);
        applied.Error.Message.ShouldContain(KillFixture.Cell);
        f.Store.Rows.Single().State.ShouldBe("dismissed");
        f.StateOf(KillFixture.Cell).ShouldBe(CellState.Paused);
    }

    [Theory]
    [InlineData("auto", "why", KillErrors.OwnerOnlyCode)]
    [InlineData("someone", "why", KillErrors.OwnerOnlyCode)]
    [InlineData(Owner, "", KillErrors.ReasonInvalidCode)]
    [InlineData(Owner, "   ", KillErrors.ReasonInvalidCode)]
    public async Task A_non_owner_or_a_missing_reason_is_refused_and_nothing_changes(string actor, string reason, string code)
    {
        var f = await ProposedAsync();

        var result = await f.Dismiss.HandleAsync(new DismissKillCommand(1, actor, reason), Ct);

        result.Error.Code.ShouldBe(code);
        f.Store.Rows.Single().State.ShouldBe("proposed");
    }

    [Fact]
    public async Task A_reason_over_400_characters_and_an_unknown_proposal_are_refused()
    {
        var f = await ProposedAsync();

        (await f.Dismiss.HandleAsync(new DismissKillCommand(1, Owner, new string('x', 401)), Ct)).Error.Code.ShouldBe(KillErrors.ReasonInvalidCode);
        (await f.Dismiss.HandleAsync(new DismissKillCommand(99, Owner, "why"), Ct)).Error.Code.ShouldBe(KillErrors.ProposalNotFoundCode);
    }
}
