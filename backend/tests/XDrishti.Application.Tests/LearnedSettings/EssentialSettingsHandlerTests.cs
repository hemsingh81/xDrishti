using NSubstitute;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.LearnedSettings;

/// <summary>AU-10 criterion 11: who and when for the Essential rows, Reset to default, and the notices.failure store.</summary>
public sealed class EssentialSettingsHandlerTests
{
    private const string Owner = "owner:me@x.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryEssentialStore _store = new();
    private readonly FakeAccountEvents _events = new();
    private readonly IChargeSheetLog _log = Substitute.For<IChargeSheetLog>();

    public EssentialSettingsHandlerTests()
    {
        _log.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new ChargeSheetLedger(false, null, []));
    }

    private ChangeEssentialSettingHandler Change() => new(_store, _events, _events, new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

    private ResetEssentialSettingHandler Reset() => new(_store, _events, _events, new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

    [Fact]
    public async Task The_five_rows_start_at_their_defaults_with_no_who_and_when()
    {
        var view = await new GetEssentialSettingsHandler(_store, _log).HandleAsync(new GetEssentialSettingsQuery(), Ct);

        view.Items.Select(i => i.Key).ShouldBe(["auto.enabled", "suggest.scope", "risk.profile", "chargesheet.gate", "notices.failure"], ignoreOrder: true);
        view.Items.ShouldAllBe(i => i.IsDefault && i.ChangedBy == null && i.ChangedAt == null);
        view.Items.Single(i => i.Key == "notices.failure").Value.ShouldBe("true");
        view.Items.Single(i => i.Key == "risk.profile").CanReset.ShouldBeFalse();
    }

    [Fact]
    public async Task Turning_failure_notices_off_needs_the_password_and_then_shows_who_and_when()
    {
        var refused = await Change().HandleAsync(new ChangeEssentialSettingCommand("notices.failure", false, Owner, FreshPassword: false), Ct);
        refused.Error.Code.ShouldBe("settings.password_required");
        (await _store.GetAsync("notices.failure", Ct)).ShouldBeNull();

        var done = await Change().HandleAsync(new ChangeEssentialSettingCommand("notices.failure", false, Owner, FreshPassword: true), Ct);

        done.IsSuccess.ShouldBeTrue();
        var view = await new GetEssentialSettingsHandler(_store, _log).HandleAsync(new GetEssentialSettingsQuery(), Ct);
        var row = view.Items.Single(i => i.Key == "notices.failure");
        (row.Value, row.IsDefault, row.ChangedBy).ShouldBe(("false", false, Owner));
        row.ChangedAt.ShouldNotBeNull();
        _events.Recorded.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Turning_failure_notices_on_needs_no_password()
    {
        var done = await Change().HandleAsync(new ChangeEssentialSettingCommand("notices.failure", true, Owner, FreshPassword: false), Ct);

        done.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Any_other_key_is_not_changed_here()
    {
        var result = await Change().HandleAsync(new ChangeEssentialSettingCommand("auto.enabled", true, Owner, FreshPassword: true), Ct);

        result.Error.Code.ShouldBe("settings.not_here");
        (await _store.GetAsync("auto.enabled", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Reset_puts_the_stored_default_back_and_marks_who_did_it()
    {
        await _store.SetAsync("notices.failure", "false", Owner, DateTimeOffset.UnixEpoch, Ct);

        var result = await Reset().HandleAsync(new ResetEssentialSettingCommand("notices.failure", Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        (await _store.GetAsync("notices.failure", Ct))!.Value.ShouldBe("true");
        result.Value.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Reset_of_the_charge_sheet_gate_is_left_to_its_own_owner_act()
    {
        var result = await Reset().HandleAsync(new ResetEssentialSettingCommand("chargesheet.gate", Owner), Ct);

        result.Error.Code.ShouldBe("settings.reset_elsewhere");
    }

    [Fact]
    public async Task Reset_of_the_risk_profile_is_not_offered_and_a_non_owner_cannot_reset()
    {
        (await Reset().HandleAsync(new ResetEssentialSettingCommand("risk.profile", Owner), Ct)).Error.Code.ShouldBe("settings.reset_not_single");
        (await Reset().HandleAsync(new ResetEssentialSettingCommand("notices.failure", "auto"), Ct)).Error.Code.ShouldBe("settings.owner_only");
        (await Reset().HandleAsync(new ResetEssentialSettingCommand("nope", Owner), Ct)).Error.Code.ShouldBe("settings.not_found");
    }

    [Fact]
    public async Task The_gate_row_reads_who_and_when_from_the_newest_gate_act()
    {
        var at = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var entry = new ChargeSheetEntry(7, ChargeSheetActKind.GateOn, null, null, null, Owner, "why", at);
        _log.ReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new ChargeSheetLedger(true, entry, [entry]));

        var view = await new GetEssentialSettingsHandler(_store, _log).HandleAsync(new GetEssentialSettingsQuery(), Ct);

        var row = view.Items.Single(i => i.Key == "chargesheet.gate");
        (row.ChangedBy, row.ChangedAt, row.IsDefault, row.CanReset).ShouldBe((Owner, at, false, false));
    }
}
