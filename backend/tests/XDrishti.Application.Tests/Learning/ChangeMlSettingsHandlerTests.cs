#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10 part 10d-1 (ADR 0220): the owner's three ML switches. Only an owner changes them; every change appends a row; a value equal to the one in force writes nothing; all three are OFF with no row.</summary>
public sealed class ChangeMlSettingsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Owner = "owner:me@x.test";

    private readonly IMlSwitchSettings _settings = Substitute.For<IMlSwitchSettings>();

    private readonly IMlSwitchStore _store = Substitute.For<IMlSwitchStore>();

    private MlSwitches _now = MlSwitches.AllOff;

    private ChangeMlSettingsHandler Handler()
    {
        _settings.CurrentAsync(Ct).Returns(_ => ValueTask.FromResult(_now));
        var next = 10L;
        _store.AppendAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct).Returns(_ => ValueTask.FromResult(next++));
        return new ChangeMlSettingsHandler(_settings, _store, NullLogger<ChangeMlSettingsHandler>.Instance);
    }

    [Fact]
    public void Every_switch_is_off_with_no_row()
    {
        MlSwitches.AllOff.Influence.ShouldBe(new MlSwitchState(null, false));
        MlSwitches.AllOff.AutoPromote.ShouldBe(new MlSwitchState(null, false));
        MlSwitches.AllOff.AutoRollback.ShouldBe(new MlSwitchState(null, false));
    }

    [Fact]
    public async Task A_change_appends_one_row_per_switch_that_changes_with_the_owner_and_the_reason()
    {
        var result = await Handler().HandleAsync(new ChangeMlSettingsCommand(Owner, true, null, true, "  I have read the evidence  "), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Changed.ShouldBe([MlSwitch.Influence, MlSwitch.AutoRollback]);
        await _store.Received(1).AppendAsync(MlSwitch.Influence, true, Owner, "I have read the evidence", Ct);
        await _store.Received(1).AppendAsync(MlSwitch.AutoRollback, true, Owner, "I have read the evidence", Ct);
        await _store.DidNotReceive().AppendAsync(MlSwitch.AutoPromote, Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct);
    }

    [Fact]
    public async Task A_value_equal_to_the_one_in_force_writes_nothing()
    {
        _now = MlRollbackFixture.On(rollback: true);

        var result = await Handler().HandleAsync(new ChangeMlSettingsCommand(Owner, false, false, true, "same"), Ct);

        result.Value.Changed.ShouldBeEmpty();
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default!, Ct);
    }

    [Fact]
    public async Task Turning_a_switch_off_is_a_row_too()
    {
        _now = MlRollbackFixture.On(promote: true);

        var result = await Handler().HandleAsync(new ChangeMlSettingsCommand(Owner, null, false, null, "off again"), Ct);

        result.Value.Changed.ShouldBe([MlSwitch.AutoPromote]);
        await _store.Received(1).AppendAsync(MlSwitch.AutoPromote, false, Owner, "off again", Ct);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("cli:xd")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task Only_an_owner_changes_a_switch_and_nothing_is_written_otherwise(string actor)
    {
        var result = await Handler().HandleAsync(new ChangeMlSettingsCommand(actor, true, true, true, "why"), Ct);

        result.Error.Code.ShouldBe(MlSafetyErrors.OwnerOnlyCode);
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default!, Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_change_needs_a_reason_and_names_at_least_one_switch(string reason)
    {
        var handler = Handler();

        (await handler.HandleAsync(new ChangeMlSettingsCommand(Owner, true, null, null, reason), Ct)).Error.Code.ShouldBe(MlSafetyErrors.SettingsInvalidCode);
        (await handler.HandleAsync(new ChangeMlSettingsCommand(Owner, true, null, null, new string('x', 501)), Ct)).Error.Code.ShouldBe(MlSafetyErrors.SettingsInvalidCode);
        (await handler.HandleAsync(new ChangeMlSettingsCommand(Owner, null, null, null, "why"), Ct)).Error.Code.ShouldBe(MlSafetyErrors.SettingsInvalidCode);
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default!, default!, Ct);
    }
}
