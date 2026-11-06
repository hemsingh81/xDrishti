#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1, D8, AC11, AC12, AC13 (ADR 0220) over the real handlers and in-memory stores: a firing trigger writes a proposal and a notice; with <c>ml.auto_rollback</c> OFF (default, no row) the registry is unchanged; ON demotes the champion to challenger as actor
/// <c>auto</c> with the switch row and the settings hash, never as an owner; the previous champion is reported and never re-promoted; one evaluation per model and day; apply is idempotent and race-safe; a trigger that is quiet or unavailable never proposes.
/// </summary>
public sealed class RollbackFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MlRollbackFixture Firing()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(7, ModelRole.Champion);
        return f;
    }

    [Fact]
    public async Task With_the_setting_off_a_firing_trigger_writes_a_proposal_and_a_notice_and_the_registry_is_unchanged()
    {
        var f = Firing();
        var rows = f.Log.Rows.Count;

        var outcome = await f.ProposeAsync(7);

        outcome.ProposalIds.ShouldBe([1L]);
        outcome.AppliedAutomatically.ShouldBe(0);
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.Proposal.Trigger, row.Proposal.ModelId).ShouldBe(("proposed", null, "calibration", 7L));
        row.Proposal.SettingsHash.Length.ShouldBe(64);
        row.Proposal.Text.ShouldContain("calibration");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        f.Log.Rows.Count.ShouldBe(rows, "no designation and no changelog row");
        await f.Checks.Received(1).InsertAsync(Arg.Any<NewMlMonitorCheck>(), Arg.Any<DateOnly>(), Ct);
        var notice = (await new RollbackProposalNotices(f.Store).ListAsync(f.Time.GetUtcNow(), Ct)).Single();
        (notice.Id, notice.Kind, notice.Severity).ShouldBe(("rollback:1", "rollback_proposed", "warning"));
    }

    [Fact]
    public async Task With_the_setting_on_the_champion_becomes_a_challenger_as_auto_with_the_switch_row_and_the_hash_and_never_as_an_owner()
    {
        var f = Firing();
        f.Switches = MlRollbackFixture.On(rollback: true);

        var outcome = await f.ProposeAsync(7);

        outcome.AppliedAutomatically.ShouldBe(1);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        var demotion = f.Log.Rows.Single(r => r.Entry.Subject == "model:7" && r.Entry.ToState == "challenger").Entry;
        demotion.Actor.ShouldBe("auto");
        demotion.FromState.ShouldBe("champion");
        demotion.EvidenceIds.ShouldBe(["model:7", "rollback-proposal:1", "ml-switch:5", "settings:" + f.Store.Rows[0].Proposal.SettingsHash[..16]]);
        demotion.Reason.ShouldContain("rollback (calibration)");
        demotion.Reason.ShouldContain("automatic, switch row 5");
        demotion.Reason.Length.ShouldBeLessThanOrEqualTo(500);
        f.Log.Rows.Any(r => r.Entry.Actor.StartsWith("owner:", StringComparison.Ordinal)).ShouldBeFalse("the owner is never recorded as the approver of an automatic decision");
        var row = f.Store.Rows.Single();
        (row.State, row.Actor, row.AutoSettingId).ShouldBe(("applied", "auto", 5L));
        var notices = await new RollbackProposalNotices(f.Store).ListAsync(f.Time.GetUtcNow(), Ct);
        notices.Select(n => (n.Id, n.Kind, n.Resolved)).ShouldBe([("rollback-auto:1", "rollback_applied_auto", true)]);
    }

    [Fact]
    public async Task An_automatic_demotion_stays_on_the_bell_for_three_days_an_owners_does_not()
    {
        var f = Firing();
        f.Switches = MlRollbackFixture.On(rollback: true);
        await f.ProposeAsync(7);
        var notices = new RollbackProposalNotices(f.Store);

        var day2 = await notices.ListAsync(f.Time.GetUtcNow().AddDays(2), Ct);
        var day4 = await notices.ListAsync(f.Time.GetUtcNow().AddDays(4), Ct);
        f.Store.Rows[0] = f.Store.Rows[0] with { Actor = "owner:me@x.test", AutoSettingId = null };
        var byOwner = await notices.ListAsync(f.Time.GetUtcNow().AddDays(2), Ct);

        day2.Single().Text.ShouldContain("automatic setting");
        day4.ShouldBeEmpty();
        byOwner.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owner_applies_a_proposal_with_the_session_only_and_the_champion_becomes_a_challenger_with_an_owner_actor()
    {
        var f = Firing();
        await f.ProposeAsync(7);

        var applied = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:me@x.test"), Ct);

        applied.IsSuccess.ShouldBeTrue();
        (applied.Value.Demoted, applied.Value.AlreadyApplied, applied.Value.Actor).ShouldBe((true, false, "owner:me@x.test"));
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        f.Log.Rows.Single(r => r.Entry.ToState == "challenger").Entry.Actor.ShouldBe("owner:me@x.test");
        (f.Store.Rows[0].State, f.Store.Rows[0].Actor, f.Store.Rows[0].AutoSettingId).ShouldBe(("applied", "owner:me@x.test", null));
    }

    [Fact]
    public async Task Applying_twice_changes_nothing_twice_and_a_model_that_is_no_longer_the_champion_is_skipped()
    {
        var f = Firing();
        await f.ProposeAsync(7);
        await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:me@x.test"), Ct);
        var rows = f.Log.Rows.Count;

        var again = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:other@x.test"), Ct);

        again.Value.AlreadyApplied.ShouldBeTrue();
        again.Value.Actor.ShouldBe("owner:me@x.test", "the first decision stays");
        f.Log.Rows.Count.ShouldBe(rows);

        // a second proposal for a model that someone else already demoted decides the proposal and moves nothing
        var g = Firing();
        await g.ProposeAsync(7);
        g.Registry.Models[7] = g.Registry.Models[7] with { Role = ModelRole.Challenger };
        var skipped = await g.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:me@x.test"), Ct);
        skipped.Value.Demoted.ShouldBeFalse();
        g.Store.Rows[0].State.ShouldBe("applied");
    }

    [Fact]
    public async Task A_model_retired_between_the_check_and_the_write_stays_retired_and_the_demotion_reports_that_nothing_changed()
    {
        var f = Firing();
        await f.ProposeAsync(7);
        f.Registry.BeforeDesignate = id =>
        {
            f.Registry.BeforeDesignate = null;
            f.Registry.Models[id] = f.Registry.Models[id] with { Role = ModelRole.Retired }; // another caller retires the champion after our read
        };

        var applied = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:me@x.test"), Ct);
        var demotion = await f.Demote.HandleAsync(new DemoteModelCommand(7, "again", "owner:me@x.test", []), Ct);

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Demoted.ShouldBeFalse("the registry refused the stale demotion");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Retired, "a retired model never re-enters the challenger pool");
        f.Registry.Designations.ShouldBeEmpty();
        demotion.Value.Changed.ShouldBeFalse();
        demotion.Value.Role.ShouldBe("retired", "the answer names the role it really has");
        f.Store.Rows[0].State.ShouldBe("applied");
    }

    [Fact]
    public async Task A_concurrent_apply_that_loses_the_race_reports_the_stored_decision_and_demotes_once()
    {
        var f = Firing();
        await f.ProposeAsync(7);
        f.Store.BeforeDecide = id =>
        {
            f.Store.BeforeDecide = null;
            f.Store.Rows[0] = f.Store.Rows[(int)id - 1] with { State = "applied", Actor = "owner:winner@x.test", DecidedAt = f.Time.GetUtcNow() };
        };

        var loser = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:loser@x.test"), Ct);

        loser.Value.AlreadyApplied.ShouldBeTrue();
        loser.Value.Actor.ShouldBe("owner:winner@x.test");
        f.Log.Rows.Count(r => r.Entry.ToState == "challenger").ShouldBe(1);
    }

    [Fact]
    public async Task The_automatic_actor_is_refused_while_the_setting_is_off_and_by_the_record_when_it_was_turned_off_in_between()
    {
        var f = Firing();
        await f.ProposeAsync(7);

        var off = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, MlActors.Auto), Ct);
        f.Switches = MlRollbackFixture.On(rollback: true);
        f.Store.BeforeDecide = _ => f.Switches = MlRollbackFixture.On(); // turned off between the check and the update
        var raced = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, MlActors.Auto), Ct);

        off.Error.Code.ShouldBe(MlSafetyErrors.RollbackAutoOffCode);
        raced.Error.Code.ShouldBe(MlSafetyErrors.RollbackDecisionRefusedCode);
        f.Store.Rows[0].State.ShouldBe("proposed");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger, "the demotion came first and stays: the safe direction");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_an_owner_or_auto_may_apply(string actor)
    {
        var f = Firing();
        await f.ProposeAsync(7);

        var result = await f.Apply.HandleAsync(new ApplyRollbackCommand(1, actor), Ct);

        result.Error.Code.ShouldBe(MlSafetyErrors.OwnerOnlyCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
    }

    [Fact]
    public async Task An_unknown_and_a_dismissed_proposal_are_refused()
    {
        var f = Firing();
        await f.ProposeAsync(7);
        f.Store.Rows[0] = f.Store.Rows[0] with { State = "dismissed", Actor = "owner:me@x.test", DecidedAt = f.Time.GetUtcNow() };

        (await f.Apply.HandleAsync(new ApplyRollbackCommand(99, "owner:me@x.test"), Ct)).Error.Code.ShouldBe(MlSafetyErrors.RollbackNotFoundCode);
        (await f.Apply.HandleAsync(new ApplyRollbackCommand(1, "owner:me@x.test"), Ct)).Error.Code.ShouldBe(MlSafetyErrors.RollbackClosedCode);
    }

    [Fact]
    public async Task The_previous_champion_is_reported_and_never_re_promoted_so_there_is_no_champion_after_the_rollback()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        var previous = f.Add(6, ModelRole.Champion, 'a');
        f.Add(7, ModelRole.Challenger, 'b');
        (await f.Designate.HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "better", "owner:me@x.test"), Ct)).IsSuccess.ShouldBeTrue();
        f.Registry.Models[6].Role.ShouldBe(ModelRole.Retired);
        await f.MakePredicateHoldAsync(previous); // the previous champion's latest evaluation satisfies the predicate without the switch
        f.Switches = MlRollbackFixture.On(rollback: true);

        var outcome = await f.ProposeAsync(7);

        outcome.AppliedAutomatically.ShouldBe(1);
        var row = f.Store.Rows.Single();
        (row.Proposal.PreviousChampionId, row.Proposal.PreviousQualifies).ShouldBe((6L, true));
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        f.Registry.Models[6].Role.ShouldBe(ModelRole.Retired, "re-promotion is the owner's act, even when the previous champion qualifies");
        f.Registry.Models.Values.Count(m => m.Role == ModelRole.Champion).ShouldBe(0, "no champion: the cell rate only");
        f.Registry.Designations.Count(d => d.Role == ModelRole.Champion).ShouldBe(1, "the only champion designation is the owner's earlier one");
    }

    [Fact]
    public async Task A_previous_champion_that_does_not_qualify_is_reported_as_not_qualifying_and_a_first_champion_has_no_previous()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration() };
        f.Add(6, ModelRole.Champion, 'a');
        f.Add(7, ModelRole.Challenger, 'b');
        await f.Designate.HandleAsync(new DesignateModelCommand(7, ModelRole.Champion, "better", "owner:me@x.test"), Ct);

        await f.ProposeAsync(7);

        (f.Store.Rows[0].Proposal.PreviousChampionId, f.Store.Rows[0].Proposal.PreviousQualifies).ShouldBe((6L, false));

        var first = Firing();
        await first.ProposeAsync(7);
        (first.Store.Rows[0].Proposal.PreviousChampionId, first.Store.Rows[0].Proposal.PreviousQualifies).ShouldBe(((long?)null, false));
    }

    [Fact]
    public async Task A_quiet_or_unavailable_trigger_never_proposes_or_demotes_even_with_the_setting_on()
    {
        var quiet = new MlRollbackFixture { Inputs = new RollbackInputs(MlRollbackFixture.MiscalibratedOutcomes(200, 0.5), []) };
        quiet.Add(7, ModelRole.Champion);
        quiet.Switches = MlRollbackFixture.On(rollback: true);
        var none = new MlRollbackFixture();
        none.Add(7, ModelRole.Champion);
        none.Switches = MlRollbackFixture.On(rollback: true);

        var a = await quiet.ProposeAsync(7);
        var b = await none.ProposeAsync(7);

        a.ProposalIds.ShouldBeEmpty();
        b.ProposalIds.ShouldBeEmpty();
        quiet.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        none.Registry.Models[7].Role.ShouldBe(ModelRole.Champion, "unavailable evidence is not a trigger");
        await none.Checks.Received(1).InsertAsync(Arg.Is<NewMlMonitorCheck>(c => c.Verdicts.Contains("Unavailable", StringComparison.Ordinal)), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task One_evaluation_per_model_and_day_a_second_run_repeats_nothing_and_a_trigger_with_an_open_proposal_makes_no_second()
    {
        var f = Firing();
        await f.ProposeAsync(7);
        f.CheckExists = true;

        var again = await f.ProposeAsync(7);
        f.CheckExists = false;
        f.Time.Advance(TimeSpan.FromDays(1));
        var nextDay = await f.ProposeAsync(7);

        again.AlreadyChecked.ShouldBeTrue();
        again.ProposalIds.ShouldBeEmpty();
        nextDay.ProposalIds.ShouldBeEmpty("the open proposal of yesterday stands");
        f.Store.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_champion_is_evaluated_and_an_unknown_model_is_an_error()
    {
        var f = Firing();
        f.Add(8, ModelRole.Challenger);

        var challenger = await f.ProposeAsync(8);
        var unknown = await f.Propose.HandleAsync(new ProposeRollbackCommand(99), Ct);

        challenger.NotChampion.ShouldBeTrue();
        f.Store.Rows.ShouldBeEmpty();
        unknown.Error.Code.ShouldBe(ModelErrors.NotFoundCode);
        await f.Checks.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, Ct);
    }

    [Fact]
    public async Task The_automatic_run_applies_only_proposals_made_today_or_whose_trigger_still_fires()
    {
        var f = Firing();
        await f.ProposeAsync(7); // an open proposal of today, setting OFF
        f.Time.Advance(TimeSpan.FromDays(1));
        f.Inputs = new RollbackInputs(MlRollbackFixture.MiscalibratedOutcomes(200, 0.5), []); // quiet now
        f.Switches = MlRollbackFixture.On(rollback: true);

        var outcome = await f.ProposeAsync(7);

        outcome.AppliedAutomatically.ShouldBe(0, "yesterday's proposal no longer fires: it stays for the owner");
        f.Store.Rows[0].State.ShouldBe("proposed");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);

        // the trigger fires again: the older open proposal is applied by the automatic run
        f.Time.Advance(TimeSpan.FromDays(1));
        f.Inputs = MlRollbackFixture.FiringCalibration();
        var again = await f.ProposeAsync(7);
        again.AppliedAutomatically.ShouldBe(1);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }
}
