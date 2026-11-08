using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08 part B, criterion 11: only the signed-in owner appends a row, with a reason, and it is audited; nothing is edited.</summary>
public sealed class ChangeShadowSwitchHandlerTests
{
    private readonly IShadowSwitchStore _store = Substitute.For<IShadowSwitchStore>();

    private readonly IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView> _chain = Substitute.For<IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView>>();

    private readonly IShadowDigestSink _sink = Substitute.For<IShadowDigestSink>();

    private readonly ITradingCalendar _calendars = Substitute.For<ITradingCalendar>();

    private readonly ShadowOptions _options = new();

    private readonly IAccountEventLog _events = Substitute.For<IAccountEventLog>();

    private readonly IAccountUnitOfWork _unitOfWork = Substitute.For<IAccountUnitOfWork>();

    private FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 6, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ChangeShadowSwitchHandlerTests()
    {
        _store.AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct).Returns(7L);
        _calendars.GetAsync(Ct).Returns(new TradingCalendar());
        Head(null);
        _sink.CheckWritableAsync(Ct).Returns((string?)null);
    }

    private void Head(DateOnly? last) => _chain.HandleAsync(Arg.Any<VerifyShadowChainQuery>(), Ct).Returns(new ShadowChainCheckView(1, 0, true, null, last, last is null ? null : new string('a', 64)));

    private ChangeShadowSwitchHandler Handler() => new(_store, _chain, _sink, _calendars, _options, _events, _unitOfWork, _time, NullLogger<ChangeShadowSwitchHandler>.Instance);

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

    [Fact]
    public async Task Turning_on_after_missed_sessions_is_refused_with_the_gap_code_and_nothing_is_written()
    {
        Head(new DateOnly(2026, 10, 30));

        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume"), Ct);

        result.Error.Code.ShouldBe(ChangeShadowSwitchHandler.GapCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _store.DidNotReceive().AppendAsync(Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Ct);
    }

    [Fact]
    public async Task The_owner_can_accept_the_gap_and_turning_off_or_a_first_start_is_never_refused()
    {
        Head(new DateOnly(2026, 10, 30));
        (await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume", true), Ct)).IsSuccess.ShouldBeTrue();
        (await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", false, "pause"), Ct)).IsSuccess.ShouldBeTrue();
        Head(null);

        (await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "first"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_gap_within_the_setting_is_not_refused()
    {
        Head(new DateOnly(2026, 11, 3));
        _options.MaxResumeGapSessions = 5;

        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume"), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    private static DateTimeOffset Utc(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0, true)] // Thursday 5 Nov before 09:00 IST: nothing since Wednesday's session has opened except Thursday itself, which has not
    [InlineData(1, 0, false)] // the one session of Thursday after 09:00 IST is missed and the limit is 0
    [InlineData(1, 1, true)] // missed equals the limit: allowed
    [InlineData(2, 1, false)] // limit plus one: refused
    public async Task The_gap_boundary_is_missed_sessions_against_the_setting(int missedSessions, int limit, bool allowed)
    {
        // Head on Wednesday 4 Nov; the clock decides how many later sessions have opened: Thursday 5 (03:30 UTC) and Friday 6.
        Head(new DateOnly(2026, 11, 4));
        _time = new FakeTimeProvider(missedSessions switch { 0 => Utc(11, 5, 3, 29), 1 => Utc(11, 5, 3, 30), _ => Utc(11, 6, 3, 30) });
        _options.MaxResumeGapSessions = limit;

        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume"), Ct);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.Code.ShouldBe(ChangeShadowSwitchHandler.GapCode);
            result.Error.Message.ShouldStartWith(missedSessions.ToString(System.Globalization.CultureInfo.InvariantCulture) + " trading session(s)");
        }
    }

    [Fact]
    public async Task A_holiday_is_not_a_missed_session()
    {
        Head(new DateOnly(2026, 11, 4));
        _calendars.GetAsync(Ct).Returns(new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 11, 5), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]));
        _time = new FakeTimeProvider(Utc(11, 5, 6, 0));

        var result = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume"), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_accepted_gap_is_audited_with_the_count()
    {
        Head(new DateOnly(2026, 10, 30));

        await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "resume", true), Ct);

        _events.Received(1).Record(Arg.Is<AccountEvent>(e => e.Detail!.Contains("gap_acknowledged missed=4", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Turning_on_is_refused_when_the_digest_folder_cannot_be_written_but_turning_off_is_not()
    {
        _sink.CheckWritableAsync(Ct).Returns("The digest folder (Shadow:DigestFolder) cannot be written by this process: UnauthorizedAccessException.");

        var on = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", true, "go"), Ct);
        var off = await Handler().HandleAsync(new ChangeShadowSwitchCommand("owner:me@example.com", false, "stop"), Ct);

        on.Error.Code.ShouldBe(ExportShadowDigestHandler.UnwritableCode);
        off.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(-1, false)]
    public void The_resume_gap_setting_is_validated_to_0_to_60(int value, bool valid)
    {
        var options = new ShadowOptions { MaxResumeGapSessions = value };

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new System.ComponentModel.DataAnnotations.ValidationContext(options), [], true).ShouldBe(valid);
    }
}
