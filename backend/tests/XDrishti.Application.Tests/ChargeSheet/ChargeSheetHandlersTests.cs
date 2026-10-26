#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.ChargeSheet;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.ChargeSheet;

/// <summary>Spec P8-23 (ADR 0127) behind the ports: the owner's act and the status, with an in-memory log that behaves like the store (newest row wins, a repeated confirmation writes nothing) and a sheet store that can be changed under a confirmation.</summary>
public sealed class ChargeSheetHandlersTests
{
    private const string Owner = "owner:test@example.com";

    private static readonly DateTimeOffset At = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeLog _log = new();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private CostSheet _stored = FixtureSheet.Sheet;

    public ChargeSheetHandlersTests()
    {
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            ValueTask.FromResult(call.Arg<string>() == _stored.Id ? Result.Success(_stored) : Result.Failure<CostSheet>(CostErrors.SheetNotFound(call.Arg<string>()))));
    }

    private RecordChargeSheetActHandler Handler() => new(_log, _log, _sheets, new ChargeSheetStatusBuilder(_log, _sheets), NullLogger<RecordChargeSheetActHandler>.Instance);

    private ValueTask<Result<ChargeSheetActOutcome>> Act(ChargeSheetActKind kind, string actor = Owner, string reason = "checked against my contract notes", string? sheet = null, string? rates = null) =>
        Handler().HandleAsync(new RecordChargeSheetActCommand(kind, actor, reason, sheet, rates), TestContext.Current.CancellationToken);

    private ValueTask<Result<ChargeSheetActOutcome>> Confirm(string? rates = null, string sheet = FixtureSheet.Id) => Act(ChargeSheetActKind.Confirm, sheet: sheet, rates: rates ?? _stored.RatesSha256);

    private async Task<ChargeSheetStatusView> Status(string? sheet = null) =>
        (await new GetChargeSheetStatusHandler(new ChargeSheetStatusBuilder(_log, _sheets)).HandleAsync(new GetChargeSheetStatusQuery(sheet), TestContext.Current.CancellationToken)).Value;

    private static CostSheet OtherRates() =>
        CostSheet.Create(FixtureSheet.Id, "FIXTURE sheet (invented numbers)", "invented for tests", null, [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA)]).Value;

    [Fact]
    public async Task Nothing_is_confirmed_and_confirming_is_off_until_the_owner_acts()
    {
        var status = await Status();

        status.State.ShouldBe("not_confirmed");
        status.CapabilityOn.ShouldBeFalse();
        status.InForce.ShouldBeNull();
        status.Text.ShouldStartWith("Not confirmed");
    }

    [Fact]
    public async Task While_confirming_is_off_a_confirmation_is_refused_with_the_right_password_and_writes_nothing()
    {
        var result = await Confirm();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("chargesheet.capability_off");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_confirming_switched_on_the_owner_confirms_one_sheet_at_its_rates_and_the_status_says_so()
    {
        (await Act(ChargeSheetActKind.GateOn)).IsSuccess.ShouldBeTrue();

        var result = await Confirm();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Changed.ShouldBeTrue();
        result.Value.Status.State.ShouldBe("confirmed");
        result.Value.Status.InForce!.SheetId.ShouldBe(FixtureSheet.Id);
        result.Value.Status.InForce.RatesSha256.ShouldBe(_stored.RatesSha256);
        result.Value.Status.Sheet!.RatesSha256.ShouldBe(_stored.RatesSha256);
        result.Value.Status.Text.ShouldContain(_stored.RatesSha256[..12]);
        _log.Rows.Select(r => r.Kind).ShouldBe([ChargeSheetActKind.GateOn, ChargeSheetActKind.Confirm]);
    }

    [Fact]
    public async Task A_confirmation_of_rates_other_than_the_stored_ones_is_a_conflict_and_writes_nothing()
    {
        await Act(ChargeSheetActKind.GateOn);

        var result = await Confirm(new string('f', 64));

        result.Error.Code.ShouldBe("chargesheet.rates_changed");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _log.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_sheet_that_is_not_stored_cannot_be_confirmed()
    {
        await Act(ChargeSheetActKind.GateOn);

        var result = await Confirm(sheet: "no-such-sheet");

        result.Error.Code.ShouldBe("chargesheet.sheet_not_found");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task A_confirmation_goes_stale_when_the_stored_sheet_gets_other_rates_and_is_confirmed_again_only_by_a_new_act()
    {
        await Act(ChargeSheetActKind.GateOn);
        await Confirm();

        _stored = OtherRates();
        var stale = await Status();
        var again = await Confirm();

        stale.State.ShouldBe("stale");
        stale.Text.ShouldStartWith("Stale");
        again.Value.Status.State.ShouldBe("confirmed");
        again.Value.Status.InForce!.RatesSha256.ShouldBe(_stored.RatesSha256);
        _log.Rows.Count(r => r.Kind == ChargeSheetActKind.Confirm).ShouldBe(2);
    }

    [Fact]
    public async Task A_confirmation_whose_sheet_is_no_longer_stored_reads_as_sheet_missing()
    {
        await Act(ChargeSheetActKind.GateOn);
        await Confirm();
        _stored = FixtureSheet.Build("another", null);

        (await Status()).State.ShouldBe("sheet_missing");
    }

    [Fact]
    public async Task The_same_confirmation_twice_writes_one_row_and_the_second_says_nothing_changed()
    {
        await Act(ChargeSheetActKind.GateOn);
        var first = await Confirm();

        var second = await Confirm();

        first.Value.Changed.ShouldBeTrue();
        second.Value.Changed.ShouldBeFalse();
        second.Value.Status.State.ShouldBe("confirmed");
        _log.Rows.Count(r => r.Kind == ChargeSheetActKind.Confirm).ShouldBe(1);
    }

    [Fact]
    public async Task A_revoke_is_another_row_and_ends_the_confirmation_also_with_confirming_switched_off()
    {
        await Act(ChargeSheetActKind.GateOn);
        await Confirm();
        await Act(ChargeSheetActKind.GateOff);

        var revoked = await Act(ChargeSheetActKind.Revoke, reason: "wrong sheet");

        revoked.Value.Changed.ShouldBeTrue();
        revoked.Value.Status.State.ShouldBe("revoked");
        revoked.Value.Status.InForce.ShouldBeNull();
        _log.Rows.Select(r => r.Kind).ShouldBe([ChargeSheetActKind.GateOn, ChargeSheetActKind.Confirm, ChargeSheetActKind.GateOff, ChargeSheetActKind.Revoke]);
        _log.Rows[^1].SheetId.ShouldBe(FixtureSheet.Id);
        _log.Rows[^1].RatesSha256.ShouldBe(_stored.RatesSha256);
    }

    [Fact]
    public async Task Revoking_when_nothing_is_confirmed_writes_nothing()
    {
        var result = await Act(ChargeSheetActKind.Revoke);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Changed.ShouldBeFalse();
        _log.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cli:xd")]
    [InlineData("rule:autonomy")]
    [InlineData("")]
    public async Task No_actor_but_the_owner_acts_and_nothing_is_written(string actor)
    {
        foreach (var kind in Enum.GetValues<ChargeSheetActKind>())
        {
            var result = await Act(kind, actor: actor, sheet: FixtureSheet.Id, rates: _stored.RatesSha256);

            result.Error.Code.ShouldBe("chargesheet.owner_only");
        }

        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_blank_reason_is_refused()
    {
        (await Act(ChargeSheetActKind.GateOn, reason: " ")).Error.Code.ShouldBe("chargesheet.reason_invalid");
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_status_shows_the_sheet_asked_for_with_the_hash_a_confirmation_must_carry_and_says_when_none_is_stored()
    {
        var shown = await Status(FixtureSheet.Id);
        var missing = await Status("nothing-here");

        shown.Sheet!.RatesSha256.ShouldBe(_stored.RatesSha256);
        shown.Sheet.Schedules.ShouldBe(2);
        shown.Sheet.Checked.ShouldBeNull();
        shown.State.ShouldBe("not_confirmed");
        missing.Sheet.ShouldBeNull();
        missing.SheetNote.ShouldBe("No stored charge sheet is named 'nothing-here'.");
    }

    [Fact]
    public async Task A_sheet_id_that_cannot_be_an_id_is_a_validation_error()
    {
        var result = await new GetChargeSheetStatusHandler(new ChargeSheetStatusBuilder(_log, _sheets)).HandleAsync(new GetChargeSheetStatusQuery("Bad Id"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("chargesheet.sheet_invalid");
        (await new GetChargeSheetStatusHandler(new ChargeSheetStatusBuilder(_log, _sheets)).HandleAsync(new GetChargeSheetStatusQuery("bad\u0007name"), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("chargesheet.sheet_invalid");
    }

    [Fact]
    public async Task The_meaning_of_confirming_is_always_in_the_status_and_says_no_order_is_placed()
    {
        var status = await Status();

        status.Meaning.ShouldBe(ChargeSheetStatusBuilder.Meaning);
        status.Meaning.ShouldContain("places no order");
        status.Meaning.ShouldContain("gross again");
    }

    /// <summary>An in-memory log with the behaviour the store promises: the newest rows decide, a repeat writes nothing, a confirmation needs the capability.</summary>
    private sealed class FakeLog : IChargeSheetLog, IChargeSheetLogWriter
    {
        public List<ChargeSheetEntry> Rows { get; } = [];

        public ValueTask<ChargeSheetLedger> ReadAsync(int historyLimit, CancellationToken cancellationToken)
        {
            var gateOn = Rows.LastOrDefault(r => r.Kind is ChargeSheetActKind.GateOn or ChargeSheetActKind.GateOff)?.Kind == ChargeSheetActKind.GateOn;
            var newest = Rows.LastOrDefault(r => ChargeSheetActKinds.IsAboutConfirmation(r.Kind));
            return ValueTask.FromResult(new ChargeSheetLedger(gateOn, newest, [.. Enumerable.Reverse(Rows).Take(historyLimit)]));
        }

        public async ValueTask<Result<ChargeSheetAppend>> AppendAsync(ChargeSheetAct act, CancellationToken cancellationToken)
        {
            var ledger = await ReadAsync(0, cancellationToken);
            if (act.Kind == ChargeSheetActKind.Confirm && !ledger.CapabilityOn)
            {
                return Result.Failure<ChargeSheetAppend>(RecordChargeSheetActHandler.CapabilityOff);
            }

            if ((act.Kind == ChargeSheetActKind.GateOn && ledger.CapabilityOn) || (act.Kind == ChargeSheetActKind.GateOff && !ledger.CapabilityOn)
                || (act.Kind == ChargeSheetActKind.Confirm && ledger.Newest is { Kind: ChargeSheetActKind.Confirm } same && same.SheetId == act.SheetId && same.RatesSha256 == act.RatesSha256)
                || (act.Kind == ChargeSheetActKind.Revoke && ledger.Newest is not { Kind: ChargeSheetActKind.Confirm }))
            {
                return Result.Success(new ChargeSheetAppend(ledger.Newest, Created: false));
            }

            var sheet = act.Kind == ChargeSheetActKind.Revoke ? ledger.Newest!.SheetId : act.SheetId;
            var rates = act.Kind == ChargeSheetActKind.Revoke ? ledger.Newest!.RatesSha256 : act.RatesSha256;
            var row = new ChargeSheetEntry(Rows.Count + 1, act.Kind, sheet, rates, act.Kind == ChargeSheetActKind.Confirm ? new DateOnly(2026, 10, 26) : null, act.Actor, act.Reason, At);
            Rows.Add(row);
            return Result.Success(new ChargeSheetAppend(row, Created: true));
        }
    }
}
