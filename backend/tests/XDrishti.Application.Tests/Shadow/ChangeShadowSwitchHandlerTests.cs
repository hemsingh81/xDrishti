using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08 part B, criterion 11: only the signed-in owner appends a row, with a reason, and it is audited; nothing is edited.</summary>
public sealed class ChangeShadowSwitchHandlerTests
{
    private readonly IShadowSwitchStore _store = Substitute.For<IShadowSwitchStore>();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ChangeShadowSwitchHandlerTests()
    {
        _store.AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct).Returns(7L);
    }

    private ChangeShadowSwitchHandler Handler() => new(_store, _events, _unitOfWork, _time, NullLogger<ChangeShadowSwitchHandler>.Instance);

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public async Task The_owner_appends_a_row_with_the_reason_and_one_audit_event_is_recorded(bool enabled, string word)
    {
        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", enabled, "  start the evidence  "), Ct);

        result.Value.ShouldBe(new ShadowSwitched(7, enabled));
        await _store.Received(1).AppendAsync(enabled, "owner:me@example.com", "start the evidence", Ct);
        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Event == AccountEventType.ShadowRecordSwitched && e.Actor == "owner:me@example.com" && e.Detail!.Contains("shadow_record=" + word, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("system:writer")]
    [InlineData("owner:")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Anyone_but_the_signed_in_owner_is_refused_and_nothing_is_written(string? actor)
    {
        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand(actor!, true, "why"), Ct);

        result.Error.Code.ShouldBe(ChangeShadowSwitchHandler.OwnerOnlyCode);
        await _store.DidNotReceive().AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_change_without_a_reason_is_refused(string? reason)
    {
        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, reason!), Ct);

        result.Error.Code.ShouldBe(ChangeShadowSwitchHandler.ReasonInvalidCode);
        await _store.DidNotReceive().AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct);
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused()
    {
        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, new string('x', 501)), Ct);

        result.Error.Code.ShouldBe(ChangeShadowSwitchHandler.ReasonInvalidCode);
    }

    [Fact]
    public async Task A_failing_audit_write_does_not_undo_the_row()
    {
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException("audit down"));

        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "why"), Ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
