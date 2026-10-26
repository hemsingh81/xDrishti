using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>Spec P8-23 (ADR 0127): the owner's act on the charge-sheet confirmation is valid only from an owner with a reason, and a confirmation stands only while the stored sheet still has the confirmed rates.</summary>
public sealed class ChargeSheetConfirmationTests
{
    private static readonly string Rates = new('a', 64);

    private static readonly string OtherRates = new('b', 64);

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private static ChargeSheetEntry Confirm(string rates, long id = 2) => new(id, ChargeSheetActKind.Confirm, "fixture-a", rates, new DateOnly(2026, 10, 26), "owner:a@b.c", "checked against my contract notes", Now);

    private static ChargeSheetEntry Revoke(long id = 3) => new(id, ChargeSheetActKind.Revoke, "fixture-a", Rates, null, "owner:a@b.c", "wrong sheet", Now);

    [Fact]
    public void A_confirmation_names_one_sheet_and_the_rates_hash_and_keeps_the_owner_and_the_reason()
    {
        var act = ChargeSheetAct.Create(ChargeSheetActKind.Confirm, "owner:a@b.c", "  checked  ", "fixture-a", Rates);

        act.IsSuccess.ShouldBeTrue();
        act.Value.SheetId.ShouldBe("fixture-a");
        act.Value.RatesSha256.ShouldBe(Rates);
        act.Value.Reason.ShouldBe("checked");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner:")]
    [InlineData("owner: ")]
    [InlineData("cli:xd")]
    [InlineData("rule:autonomy")]
    [InlineData("system:nightly")]
    public void Only_an_owner_actor_acts_and_every_other_actor_is_forbidden(string? actor)
    {
        foreach (var kind in Enum.GetValues<ChargeSheetActKind>())
        {
            var act = ChargeSheetAct.Create(kind, actor, "why", "fixture-a", Rates);

            act.IsFailure.ShouldBeTrue();
            act.Error.Code.ShouldBe("chargesheet.owner_only");
        }
    }

    [Fact]
    public void An_owner_actor_longer_than_the_column_is_refused()
    {
        ChargeSheetAct.Create(ChargeSheetActKind.GateOn, "owner:" + new string('x', 95), "why").Error.Code.ShouldBe("chargesheet.owner_only");
        ChargeSheetAct.Create(ChargeSheetActKind.GateOn, "owner:" + new string('x', 94), "why").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_required(string? reason)
    {
        ChargeSheetAct.Create(ChargeSheetActKind.Revoke, "owner:a@b.c", reason).Error.Code.ShouldBe("chargesheet.reason_invalid");
    }

    [Fact]
    public void A_reason_has_at_most_500_characters()
    {
        ChargeSheetAct.Create(ChargeSheetActKind.Revoke, "owner:a@b.c", new string('r', 500)).IsSuccess.ShouldBeTrue();
        ChargeSheetAct.Create(ChargeSheetActKind.Revoke, "owner:a@b.c", new string('r', 501)).Error.Code.ShouldBe("chargesheet.reason_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Fixture-A")]
    [InlineData("-x")]
    [InlineData("test-a")]
    [InlineData("a b")]
    public void A_confirmation_needs_a_storable_sheet_id_and_never_a_test_sheet(string? sheet)
    {
        ChargeSheetAct.Create(ChargeSheetActKind.Confirm, "owner:a@b.c", "why", sheet, Rates).Error.Code.ShouldBe("chargesheet.sheet_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_confirmation_needs_the_rates_hash_as_64_lower_case_hex(string? rates)
    {
        ChargeSheetAct.Create(ChargeSheetActKind.Confirm, "owner:a@b.c", "why", "fixture-a", rates).Error.Code.ShouldBe("chargesheet.rates_invalid");
    }

    [Fact]
    public void A_revoke_and_a_capability_change_carry_no_sheet_and_no_hash_even_when_given_one()
    {
        foreach (var kind in new[] { ChargeSheetActKind.Revoke, ChargeSheetActKind.GateOn, ChargeSheetActKind.GateOff })
        {
            var act = ChargeSheetAct.Create(kind, "owner:a@b.c", "why", "fixture-a", Rates);

            act.Value.SheetId.ShouldBeNull();
            act.Value.RatesSha256.ShouldBeNull();
        }
    }

    [Fact]
    public void The_stored_words_round_trip_and_an_unknown_word_is_no_act()
    {
        foreach (var kind in Enum.GetValues<ChargeSheetActKind>())
        {
            ChargeSheetActKinds.Parse(ChargeSheetActKinds.Word(kind)).ShouldBe(kind);
        }

        ChargeSheetActKinds.Parse("approve").ShouldBeNull();
        ChargeSheetActKinds.Parse(null).ShouldBeNull();
    }

    [Fact]
    public void With_no_act_nothing_is_confirmed()
    {
        ChargeSheetStandings.Evaluate(null, Rates).ShouldBe(ChargeSheetState.NotConfirmed);
        ChargeSheetStandings.Evaluate(null, null).ShouldBe(ChargeSheetState.NotConfirmed);
    }

    [Fact]
    public void A_confirmation_stands_only_while_the_stored_sheet_has_exactly_the_confirmed_rates()
    {
        ChargeSheetStandings.Evaluate(Confirm(Rates), Rates).ShouldBe(ChargeSheetState.Confirmed);
        ChargeSheetStandings.Evaluate(Confirm(Rates), OtherRates).ShouldBe(ChargeSheetState.Stale);
        ChargeSheetStandings.Evaluate(Confirm(Rates), null).ShouldBe(ChargeSheetState.SheetMissing);
    }

    [Fact]
    public void A_revoke_ends_the_confirmation_whatever_the_sheet_has()
    {
        ChargeSheetStandings.Evaluate(Revoke(), Rates).ShouldBe(ChargeSheetState.Revoked);
        ChargeSheetStandings.Evaluate(Revoke(), OtherRates).ShouldBe(ChargeSheetState.Revoked);
        ChargeSheetStandings.Evaluate(Revoke(), null).ShouldBe(ChargeSheetState.Revoked);
    }

    [Fact]
    public void A_row_that_is_not_a_complete_confirmation_is_never_read_as_one()
    {
        var capability = new ChargeSheetEntry(1, ChargeSheetActKind.GateOn, null, null, null, "owner:a@b.c", "on", Now);
        var noDate = Confirm(Rates) with { ConfirmedOn = null };
        var noRates = Confirm(Rates) with { RatesSha256 = null };

        ChargeSheetStandings.Evaluate(capability, Rates).ShouldBe(ChargeSheetState.NotConfirmed);
        ChargeSheetStandings.Evaluate(noDate, Rates).ShouldBe(ChargeSheetState.NotConfirmed);
        ChargeSheetStandings.Evaluate(noRates, Rates).ShouldBe(ChargeSheetState.NotConfirmed);
    }

    [Fact]
    public void Every_state_has_a_distinct_word()
    {
        Enum.GetValues<ChargeSheetState>().Select(ChargeSheetStandings.Word).Distinct().Count().ShouldBe(Enum.GetValues<ChargeSheetState>().Length);
    }
}
