using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>The class of a ledger entry from Dhan's voucher description and narration (ADR 0025): table-driven over made-up samples shaped like the ones in Dhan's documentation; anything unrecognised is <c>other</c>.</summary>
public sealed class LedgerClassifierTests
{
    [Theory]
    [InlineData("RECEIPT", "FUNDS DEPOSIT THROUGH UPI", 25000, LedgerClass.FundsIn)]
    [InlineData("RECEIPT", "Funds added via Net Banking", 5000.50, LedgerClass.FundsIn)]
    [InlineData("RECEIPT", "NEFT PAYIN", 10000, LedgerClass.FundsIn)]
    [InlineData("PAYBLE", "FUNDS WITHDRAWAL", -20000, LedgerClass.FundsOut)]
    [InlineData("PAYMENT", "Payout to bank account", -7500, LedgerClass.FundsOut)]
    [InlineData("RECEIPT", "DIVIDEND CREDIT EXMPL FINAL DIV", 1500, LedgerClass.Dividend)]
    [InlineData("JOURNAL", "Div. payout EXMPL INE000A01011", 80, LedgerClass.Dividend)]
    [InlineData("JOURNAL", "DP CHARGES 02-09-2026 EXMPL", -15.93, LedgerClass.Charges)]
    [InlineData("JOURNAL", "ANNUAL MAINTENANCE CHARGES (AMC) FY 2026", -354, LedgerClass.Charges)]
    [InlineData("JOURNAL", "Brokerage and GST for the month", -118.4, LedgerClass.Charges)]
    [InlineData("BILL", "BILL FOR 02-SEP-2026 NSE-CAPITAL EQUITY PURCHASE", -9000, LedgerClass.Other)]
    [InlineData("BILL", "Sale settlement 20-SEP-2026", 11600, LedgerClass.Other)]
    [InlineData("", "", 100, LedgerClass.Other)]
    public void An_entry_gets_the_class_its_texts_say(string description, string narration, double amount, LedgerClass expected)
    {
        LedgerClassifier.Classify(description, narration, (decimal)amount).ShouldBe(expected);
    }

    [Theory]
    [InlineData("FUNDS WITHDRAWAL REVERSAL", 20000)] // money coming back is not a payout
    [InlineData("DIVIDEND RECOVERY", -80)] // a debit is not a dividend
    [InlineData("FUNDS DEPOSIT", -100)] // a debit is not a deposit
    [InlineData("DP CHARGES REVERSAL", 15.93)] // a credit is not a charge
    public void A_text_that_contradicts_the_sign_of_the_amount_is_other(string narration, double amount)
    {
        LedgerClassifier.Classify(string.Empty, narration, (decimal)amount).ShouldBe(LedgerClass.Other);
    }

    [Theory]
    [InlineData("Trade settlement for the fortnight")] // "stt" must not be found inside "settlement"
    [InlineData("Supine position adjustment")] // "upi" must not be found inside another word
    [InlineData("Gamcom journal")] // "amc" must not be found inside another word
    public void A_short_phrase_is_found_only_as_a_word(string narration)
    {
        LedgerClassifier.Classify(string.Empty, narration, -100m).ShouldBe(LedgerClass.Other);
        LedgerClassifier.Classify(string.Empty, narration, 100m).ShouldBe(LedgerClass.Other);
    }

    [Fact]
    public void A_charge_that_names_a_payout_is_a_charge_and_not_funds_out()
    {
        LedgerClassifier.Classify("JOURNAL", "Payout charge for instant withdrawal", -10m).ShouldBe(LedgerClass.Charges);
    }

    [Fact]
    public void Case_does_not_matter_and_the_description_counts_as_well_as_the_narration()
    {
        LedgerClassifier.Classify("funds deposit", "x", 10m).ShouldBe(LedgerClass.FundsIn);
        LedgerClassifier.Classify("X", "FUNDS DEPOSIT", 10m).ShouldBe(LedgerClass.FundsIn);
    }
}
