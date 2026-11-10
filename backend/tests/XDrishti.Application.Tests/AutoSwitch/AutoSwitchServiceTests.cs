using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.AutoSwitch;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.AutoSwitch;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Application.Tests.LearnedSettings;

namespace XDrishti.Application.Tests.AutoSwitch;

/// <summary>Spec AU-17: the Auto switch (ADR 0252): preconditions, the chain, restore-exact-state, the record and the readout.</summary>
public sealed class AutoSwitchServiceTests
{
    private const string Owner = AutoSwitchHarness.Owner;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly AutoSwitchHarness _h = new();

    [Fact]
    public async Task Auto_is_off_by_default_and_the_chain_is_not_complete()
    {
        var view = await _h.Service().ReadAsync(Ct);

        view.On.ShouldBeFalse();
        view.Complete.ShouldBeFalse();
        view.Chain.Select(c => c.ServiceId).ShouldBe(AutoSwitchHarness.Chain);
    }

    [Fact]
    public async Task Turning_on_without_a_fresh_password_is_refused_and_changes_nothing()
    {
        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, FreshPassword: false), Ct);

        result.Error.Code.ShouldBe("auto.password_required");
        _h.Enabled.Values.ShouldAllBe(on => !on);
        _h.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("system:nightly")]
    public async Task Only_the_owner_turns_auto_on(string actor)
    {
        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(actor, FreshPassword: true), Ct);

        result.Error.Code.ShouldBe("auto.owner_only");
        _h.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_on_resumes_the_paused_chain_services_and_records_the_owner_with_a_snapshot()
    {
        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.On.ShouldBeTrue();
        result.Value.Complete.ShouldBeTrue();
        result.Value.Builder.ShouldBe("selection");
        _h.Enabled.Values.ShouldAllBe(on => on);
        var row = _h.Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe("on");
        row.Actor.ShouldBe(Owner);
        row.Changed.ShouldBe(AutoSwitchHarness.Chain);
        row.Snapshot["auto.enabled.before"].ShouldBe("false");
        row.Snapshot["auto.enabled.after"].ShouldBe("true");
        row.Snapshot["service.eod.before"].ShouldBe("paused");
        (await _h.Store.GetAsync("auto.enabled", Ct))!.Value.ShouldBe("true");
        _h.ServiceActors.ShouldAllBe(a => a == Owner);
    }

    [Fact]
    public async Task Turning_off_pauses_only_what_on_resumed_and_keeps_the_owners_own_choices()
    {
        _h.Enabled["eod"] = true;
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);
        _h.Rows[0].Changed.ShouldNotContain("eod");

        var result = await service.TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);

        result.Value.On.ShouldBeFalse();
        _h.Enabled["eod"].ShouldBeTrue();
        _h.Enabled.Where(e => e.Key != "eod").ShouldAllBe(e => !e.Value);
        _h.Rows[1].Kind.ShouldBe("off");
        _h.Rows[1].Changed.ShouldBe(AutoSwitchHarness.Chain.Where(id => id != "eod"));
        (await _h.Store.GetAsync("auto.enabled", Ct))!.Value.ShouldBe("false");
    }

    [Fact]
    public async Task Turning_off_leaves_a_service_the_owner_already_paused_alone_and_does_not_resume_anything()
    {
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);
        _h.Enabled["nightly"] = false;

        await service.TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);

        _h.Rows[1].Changed.ShouldNotContain("nightly");
        _h.Enabled.Values.ShouldAllBe(on => !on);
    }

    [Fact]
    public async Task Two_ons_then_one_off_pauses_everything_either_resumed()
    {
        _h.Rows.Add(new AutoSwitchChange(1, "on", Owner, DateTimeOffset.UnixEpoch, ["eod", "nightly"], new Dictionary<string, string>()));
        _h.Rows.Add(new AutoSwitchChange(2, "on", Owner, DateTimeOffset.UnixEpoch, ["weekly", "suggestions"], new Dictionary<string, string>()));
        _h.Enabled["eod"] = _h.Enabled["nightly"] = _h.Enabled["weekly"] = _h.Enabled["suggestions"] = true;
        await _h.Store.SetAsync("auto.enabled", "true", Owner, DateTimeOffset.UnixEpoch, Ct);

        await _h.Service().TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);

        _h.Enabled.Values.ShouldAllBe(on => !on);
    }

    [Fact]
    public async Task A_partial_off_leaves_auto_on_and_the_next_off_finishes_the_job()
    {
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);
        _h.FailPause.Add("weekly");

        var first = await service.TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);
        _h.FailPause.Clear();
        var second = await service.TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);

        first.Error.Code.ShouldBe("auto.chain_failed");
        second.Value.On.ShouldBeFalse();
        _h.Enabled.Values.ShouldAllBe(on => !on);
    }

    [Fact]
    public async Task Turning_off_when_already_off_writes_nothing()
    {
        var result = await _h.Service().TurnOffAsync(new TurnAutoOffCommand(Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        _h.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_on_when_already_on_writes_nothing_more()
    {
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        _h.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_service_paused_later_shows_the_chain_as_incomplete_with_its_name()
    {
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);
        _h.Enabled["suggestions"] = false;

        var view = await service.ReadAsync(Ct);

        view.On.ShouldBeTrue();
        view.Complete.ShouldBeFalse();
        view.Note!.ShouldContain("suggestions");
        view.Chain.Single(c => c.ServiceId == "suggestions").Enabled.ShouldBeFalse();
        view.Chain.Single(c => c.ServiceId == "suggestions").ResumedByAuto.ShouldBeTrue();
    }

    [Fact]
    public async Task No_scope_is_refused_and_named()
    {
        _h.Scopes.Row = null;

        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.Error.Code.ShouldBe("auto.preconditions_failed");
        result.Error.Message.ShouldContain("scope");
        _h.Enabled.Values.ShouldAllBe(on => !on);
        _h.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_data_account_that_is_not_connected_is_refused_and_named()
    {
        _h.DataAccount.GetAsync(Arg.Any<CancellationToken>()).Returns(new DataAccountState(null, null, DataAccountStatuses.None, "connect a Data account"));

        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.Error.Message.ShouldContain("Data account");
        result.Error.Message.ShouldContain("connect a Data account");
    }

    [Fact]
    public async Task An_unconfirmed_charge_sheet_is_refused_and_named()
    {
        _h.Charges.FindAsync(Arg.Any<CancellationToken>()).Returns((XDrishti.Domain.Reporting.ChargeSheetConfirmation?)null);

        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.Error.Message.ShouldContain("charge sheet");
    }

    [Fact]
    public async Task An_input_that_cannot_be_read_is_unavailable_and_never_a_pass()
    {
        _h.DataAccount.GetAsync(Arg.Any<CancellationToken>()).Returns<DataAccountState>(_ => throw new InvalidOperationException("down"));

        var view = await _h.Service().ReadAsync(Ct);
        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        view.Preconditions.Single(p => p.Id == "data-account").Status.ShouldBe("unavailable");
        result.IsFailure.ShouldBeTrue();
        _h.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_failing_item_is_named_together()
    {
        _h.Scopes.Row = null;
        _h.Charges.FindAsync(Arg.Any<CancellationToken>()).Returns((XDrishti.Domain.Reporting.ChargeSheetConfirmation?)null);

        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.Error.Message.ShouldContain("scope");
        result.Error.Message.ShouldContain("charge sheet");
    }

    [Fact]
    public async Task A_chain_service_that_cannot_be_resumed_puts_back_what_was_resumed_and_leaves_auto_off()
    {
        _h.FailResume.Add("nightly");

        var result = await _h.Service().TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);

        result.Error.Code.ShouldBe("auto.chain_failed");
        _h.Enabled.Values.ShouldAllBe(on => !on);
        _h.Rows.ShouldBeEmpty();
        (await _h.Store.GetAsync("auto.enabled", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_fail_closed_last_night_is_shown_with_its_reason()
    {
        var run = new NewPlanRun(new DateOnly(2026, 11, 10), null, "failed_closed", "eod", "EOD is stale.", null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        _h.Plans.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<PlanRunRow> { new(1, 1, run) });

        var view = await _h.Service().ReadAsync(Ct);

        view.Night.State.ShouldBe("failed_closed");
        view.Night.Reason.ShouldBe("EOD is stale.");
    }

    [Fact]
    public async Task The_essential_row_turns_auto_on_only_with_a_fresh_password_and_off_with_a_session()
    {
        var handler = new ChangeEssentialSettingHandler(_h.Store, _h.Service(), new FakeAccountEvents(), new FakeAccountEvents(), new FakeTimeProvider(), NullLogger<ChangeEssentialSettingHandler>.Instance);

        var refused = await handler.HandleAsync(new ChangeEssentialSettingCommand("auto.enabled", true, Owner, FreshPassword: false), Ct);
        var on = await handler.HandleAsync(new ChangeEssentialSettingCommand("auto.enabled", true, Owner, FreshPassword: true), Ct);
        var off = await handler.HandleAsync(new ChangeEssentialSettingCommand("auto.enabled", false, Owner, FreshPassword: false), Ct);

        refused.Error.Code.ShouldBe("auto.password_required");
        on.Value.Value.ShouldBe("true");
        off.Value.Value.ShouldBe("false");
        _h.Enabled.Values.ShouldAllBe(e => !e);
    }

    [Fact]
    public async Task Reset_to_default_turns_auto_off_through_the_switch()
    {
        var service = _h.Service();
        await service.TurnOnAsync(new TurnAutoOnCommand(Owner, true), Ct);
        var handler = new ResetEssentialSettingHandler(_h.Store, service, new FakeAccountEvents(), new FakeAccountEvents(), new FakeTimeProvider(), NullLogger<ResetEssentialSettingHandler>.Instance);

        var result = await handler.HandleAsync(new ResetEssentialSettingCommand("auto.enabled", Owner), Ct);

        result.Value.IsDefault.ShouldBeTrue();
        _h.Enabled.Values.ShouldAllBe(e => !e);
    }

    [Fact]
    public async Task The_switched_builder_builds_the_skeleton_while_auto_is_off()
    {
        var request = new PlanBuildRequest(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var builder = new SwitchedPlanBuilder(_h, null!, new XDrishti.Application.Planning.NullPlanBuilder());

        var built = await builder.BuildAsync(request, Ct);

        built.Value.Basis.ShouldBe(XDrishti.Application.Planning.NullPlanBuilder.Basis);
    }
}
