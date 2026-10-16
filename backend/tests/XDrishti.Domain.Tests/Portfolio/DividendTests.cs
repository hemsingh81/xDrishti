using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>A dividend the owner entered and the decision about a suggested one (ADR 0026): what they guard, and the rules for dividend values.</summary>
public sealed class DividendTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static Dividend Entered(
        decimal amount = 1234.567m, decimal? perShare = 12.34567m, string? note = "  final\tdividend ", string? isin = "ine000a01011", string? symbol = "nse:exmpl-eq", Guid? holding = null) =>
        Dividend.Create(Account, holding, null, isin, symbol, new DateOnly(2026, 6, 15), amount, perShare, note, " owner@example.com ", HoldingData.T0);

    [Fact]
    public void A_dividend_rounds_the_amount_to_the_paisa_the_per_share_to_four_decimals_and_cleans_the_texts()
    {
        var dividend = Entered(holding: Guid.NewGuid());

        (dividend.Amount, dividend.PerShare, dividend.Note, dividend.Isin, dividend.TradingSymbol, dividend.CreatedBy, dividend.Source, dividend.IsVoided).ShouldBe(
            (1234.57m, 12.3457m, "final dividend", "INE000A01011", "EXMPL", "owner@example.com", DividendSource.Manual, false));
        dividend.HoldingId.ShouldNotBeNull();
    }

    [Fact]
    public void A_dividend_of_no_holding_has_no_security_and_a_blank_note_is_none()
    {
        var dividend = Entered(isin: null, symbol: " ", note: "  \t ", perShare: null);

        (dividend.HoldingId, dividend.Isin, dividend.TradingSymbol, dividend.Note, dividend.PerShare).ShouldBe((null, null, null, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.004)]
    public void A_dividend_has_an_amount_above_nothing(double amount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Entered(amount: (decimal)amount));
    }

    [Fact]
    public void A_dividend_has_limits_for_the_amount_the_per_share_the_note_and_the_account()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Entered(amount: DividendRules.MaxAmount + 1m));
        Should.Throw<ArgumentOutOfRangeException>(() => Entered(perShare: 0m));
        Should.Throw<ArgumentOutOfRangeException>(() => Entered(perShare: DividendRules.MaxPerShare + 1m));
        Should.Throw<ArgumentException>(() => Entered(note: new string('x', DividendRules.MaxNoteLength + 1)));
        Entered(note: new string('x', DividendRules.MaxNoteLength)).Note!.Length.ShouldBe(DividendRules.MaxNoteLength);
        Should.Throw<ArgumentException>(() => Dividend.Create(Guid.Empty, null, null, null, null, new DateOnly(2026, 6, 15), 1m, null, null, "o", HoldingData.T0));
    }

    [Fact]
    public void A_dividend_is_voided_once_and_keeps_who_and_when_of_the_first_time()
    {
        var dividend = Entered();

        var first = dividend.Void(" owner@example.com ", HoldingData.T0.AddHours(1));
        var second = dividend.Void("someone else", HoldingData.T0.AddHours(2));

        (first, second, dividend.IsVoided, dividend.VoidedBy, dividend.VoidedAt).ShouldBe((true, false, true, "owner@example.com", HoldingData.T0.AddHours(1)));
    }

    [Fact]
    public void An_unnamed_actor_is_unknown_and_a_long_one_is_cut()
    {
        var dividend = Dividend.Create(Account, null, null, null, null, new DateOnly(2026, 6, 15), 1m, null, null, " ", HoldingData.T0);

        dividend.CreatedBy.ShouldBe("unknown");
        dividend.Void(new string('a', 400), HoldingData.T0);
        dividend.VoidedBy!.Length.ShouldBe(Dividend.MaxCreatedByLength);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-10", true)]
    [InlineData("2026-10-11", "2026-10-10", false)]
    [InlineData("1990-01-02", "2026-10-10", true)]
    [InlineData("1990-01-01", "2026-10-10", false)]
    public void The_pay_date_is_from_1990_to_today(string date, string today, bool allowed)
    {
        var errors = new FieldErrors();

        DividendRules.CheckPayDate(errors, "payDate", DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

        errors.HasErrors.ShouldBe(!allowed);
    }

    [Fact]
    public void A_decision_is_about_one_ledger_entry_and_names_the_dividend_made_from_it()
    {
        var entry = Guid.NewGuid();
        var dividend = Guid.NewGuid();

        var accepted = DividendSuggestion.Accept(entry, Account, dividend, " owner@example.com ", HoldingData.T0);
        var dismissed = DividendSuggestion.Dismiss(entry, Account, "", HoldingData.T0);

        (accepted.Id, accepted.State, accepted.DividendId, accepted.AccountId, accepted.DecidedBy, accepted.DecidedAt).ShouldBe((entry, DividendSuggestionState.Accepted, dividend, Account, "owner@example.com", HoldingData.T0));
        (dismissed.State, dismissed.DividendId, dismissed.DecidedBy).ShouldBe((DividendSuggestionState.Dismissed, null, "unknown"));
        Should.Throw<ArgumentException>(() => DividendSuggestion.Dismiss(Guid.Empty, Account, "o", HoldingData.T0));
        Should.Throw<ArgumentException>(() => DividendSuggestion.Dismiss(entry, Guid.Empty, "o", HoldingData.T0));
    }
}
