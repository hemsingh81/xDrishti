using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>Which ledger entries of class charges are costs of the account and not of trades (ADR 0026): the costs of trades are on the trade rows and must never be counted a second time.</summary>
public sealed class LedgerCostsTests
{
    [Theory]
    [InlineData("DEBIT", "DP charges for sale of 10 shares")]
    [InlineData("DEBIT", "DP CHARGES + GST")]
    [InlineData("DEBIT", "Annual Maintenance Charges FY 2025-26")]
    [InlineData("DEBIT", "AMC 01/04/2026")]
    [InlineData("DPC Interest", "DPC Interest : 01/09/2026 to 30/09/2026")]
    [InlineData("DEBIT", "Delayed payment charges")]
    [InlineData("DEBIT", "Penalty for short margin")]
    [InlineData("DEBIT", "Platform fee")]
    [InlineData("DEBIT", "Pledge charges")]
    [InlineData("DEBIT", "Unpledge charges")]
    [InlineData("DEBIT", "Payout charges")]
    [InlineData("DEBIT", "Withdrawal charges")]
    [InlineData("DEBIT", "Demat charges")]
    [InlineData("DEBIT", "Call & Trade charges")]
    public void The_costs_of_the_account_as_such_are_counted(string description, string narration)
    {
        LedgerCosts.IsAccountLevel(description, narration).ShouldBeTrue();
        LedgerClassifier.Classify(description, narration, -10m).ShouldBe(LedgerClass.Charges);
    }

    [Theory]
    [InlineData("DEBIT", "Brokerage")]
    [InlineData("DEBIT", "STT on sell")]
    [InlineData("DEBIT", "Securities Transaction Tax")]
    [InlineData("DEBIT", "GST")]
    [InlineData("DEBIT", "IGST @18%")]
    [InlineData("DEBIT", "Stamp duty")]
    [InlineData("DEBIT", "SEBI turnover charges")]
    [InlineData("DEBIT", "Exchange transaction charges")]
    [InlineData("BILL", "Bill for 12/10/2026: brokerage, STT, GST")]
    [InlineData("BILL", "Bill 2026-10-12 incl. DP charges")]
    [InlineData("DEBIT", "Settlement of trades 2026-10-12 (brokerage 12.00, stamp duty 3.00)")]
    [InlineData("DEBIT", "Net obligation of the day")]
    [InlineData("DEBIT", "Contract note 4521 charges")]
    [InlineData("DEBIT", "Charges")]
    [InlineData("", "")]
    public void The_costs_of_trades_and_the_settlement_narrations_are_never_counted(string description, string narration)
    {
        LedgerCosts.IsAccountLevel(description, narration).ShouldBeFalse();
    }

    [Fact]
    public void The_texts_are_untrusted_and_may_be_missing()
    {
        LedgerCosts.IsAccountLevel(null, null).ShouldBeFalse();
        LedgerCosts.IsAccountLevel(null, "DP charges").ShouldBeTrue();
        LedgerCosts.IsAccountLevel("DP charges", null).ShouldBeTrue();
    }

    [Fact]
    public void A_phrase_inside_another_word_is_not_a_phrase()
    {
        LedgerCosts.IsAccountLevel("DEBIT", "Lamcorp settlement").ShouldBeFalse();
        LedgerCosts.IsAccountLevel("DEBIT", "Bills payable platform fee").ShouldBeTrue(); // "bills" is not the word "bill"
    }
}
