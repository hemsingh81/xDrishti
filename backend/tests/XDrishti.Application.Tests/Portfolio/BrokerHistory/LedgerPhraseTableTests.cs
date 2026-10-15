using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>The one table of the charge phrases (ADR 0030): the classifier, the account-level cost rule and the depository test read it, so they cannot disagree, and it holds no phrase another phrase of the same kind already finds.</summary>
public sealed class LedgerPhraseTableTests
{
    private static readonly LedgerText.Kinds[] AllKinds = [LedgerText.Kinds.Charge, LedgerText.Kinds.Depository, LedgerText.Kinds.AccountLevel, LedgerText.Kinds.TradeRelated];

    [Fact]
    public void A_phrase_is_listed_once_in_lower_case_and_has_at_least_one_kind()
    {
        var phrases = LedgerText.Phrases;

        phrases.Select(p => p.Text).Distinct(StringComparer.Ordinal).Count().ShouldBe(phrases.Count);
        phrases.ShouldAllBe(p => string.Equals(p.Text, p.Text.ToLowerInvariant(), StringComparison.Ordinal) && p.Text == p.Text.Trim() && p.Kinds != LedgerText.Kinds.None);
    }

    [Fact]
    public void No_phrase_is_dead_because_another_phrase_of_the_same_kind_already_finds_every_text_it_finds()
    {
        var dead = new List<string>();
        foreach (var kind in AllKinds)
        {
            var ofKind = LedgerText.Phrases.Where(p => p.Kinds.HasFlag(kind)).ToList();
            foreach (var phrase in ofKind)
            {
                // "dp charges" beside "dp charge": the text of the phrase itself is found by the other one.
                dead.AddRange(ofKind.Where(other => other != phrase && LedgerText.Has(phrase.Text, other.Text)).Select(other => $"{kind}: '{phrase.Text}' is found by '{other.Text}'"));
            }
        }

        dead.ShouldBeEmpty();
    }

    [Fact]
    public void Every_account_level_and_depository_phrase_that_names_a_charge_or_fee_makes_the_entry_a_charge_except_the_listed_qualifiers()
    {
        // These only qualify an entry that another phrase has made a charge (as before the table was one): "depository" alone could be a transfer, the fees are named beside GST or another charge.
        string[] qualifiers = ["depository", "dp fee", "demat fee", "subscription fee"];

        var notCharges = LedgerText.Phrases
            .Where(p => p.Kinds.HasFlag(LedgerText.Kinds.AccountLevel) || p.Kinds.HasFlag(LedgerText.Kinds.Depository))
            .Where(p => !p.Kinds.HasFlag(LedgerText.Kinds.Charge))
            .Select(p => p.Text);

        notCharges.Order(StringComparer.Ordinal).ShouldBe(qualifiers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_trade_related_phrase_is_never_an_account_level_one()
    {
        LedgerText.Phrases.Where(p => p.Kinds.HasFlag(LedgerText.Kinds.TradeRelated) && p.Kinds.HasFlag(LedgerText.Kinds.AccountLevel)).ShouldBeEmpty();
    }

    // description, narration, charge (class charges for a debit), depository, account level
    [Theory]
    [InlineData("DEBIT", "DP charges for sale of 10 shares", true, true, true)]
    [InlineData("DEBIT", "DP CHARGES + GST", true, true, true)]
    [InlineData("DEBIT", "Demat charges", true, true, true)]
    [InlineData("DEBIT", "Depository charges", true, true, true)]
    [InlineData("DEBIT", "DP fee incl. GST", true, true, false)] // GST makes it a charge; the fee phrase makes it a DP charge; GST alone is not account level
    [InlineData("DEBIT", "Demat fee + STT", true, true, false)]
    [InlineData("DEBIT", "Depository services incl. GST", true, true, false)]
    [InlineData("DEBIT", "DP fee", false, true, false)] // by itself it is no charge (the class stays other)
    [InlineData("DEBIT", "Subscription fee", false, false, true)]
    [InlineData("DEBIT", "Subscription fee incl. GST", true, false, true)]
    [InlineData("DPC Interest", "DPC Interest : 01/09/2026 to 30/09/2026", true, false, true)]
    [InlineData("DEBIT", "DPC Int. 01/09/2026", true, false, true)]
    [InlineData("DEBIT", "AMC 01/04/2026", true, false, true)]
    [InlineData("DEBIT", "Annual Maintenance Charges FY 2025-26", true, false, true)]
    [InlineData("DEBIT", "Account opening charges", true, false, true)]
    [InlineData("DEBIT", "Late payment charges", true, false, true)]
    [InlineData("DEBIT", "Penalty for short margin", true, false, true)]
    [InlineData("DEBIT", "Platform fee", true, false, true)]
    [InlineData("DEBIT", "Pledge charges", true, false, true)]
    [InlineData("DEBIT", "Unpledge charges", true, false, true)]
    [InlineData("DEBIT", "Payout charges", true, false, true)]
    [InlineData("DEBIT", "Withdrawal charges", true, false, true)]
    [InlineData("DEBIT", "Call & Trade charges", true, false, true)]
    [InlineData("DEBIT", "Call and trade", true, false, true)]
    [InlineData("DEBIT", "Brokerage", true, false, false)]
    [InlineData("DEBIT", "STT on sell", true, false, false)]
    [InlineData("DEBIT", "Securities Transaction Tax", true, false, false)]
    [InlineData("DEBIT", "Stamp duty", true, false, false)]
    [InlineData("DEBIT", "SEBI turnover charges", true, false, false)]
    [InlineData("DEBIT", "Exchange transaction charges", true, false, false)]
    [InlineData("DEBIT", "GST", true, false, false)]
    [InlineData("DEBIT", "IGST @18%", true, false, false)]
    [InlineData("DEBIT", "CGST 9% SGST 9%", true, false, false)]
    [InlineData("DEBIT", "Turnover charges", true, false, false)]
    [InlineData("DEBIT", "Settlement of trades 2026-10-12 (DP charges 12.00)", true, true, false)] // a settlement narration is a trade's: never an account cost
    [InlineData("BILL", "Bill 2026-10-12 incl. DP charges", true, true, false)]
    [InlineData("DEBIT", "Net obligation of the day", false, false, false)]
    [InlineData("DEBIT", "Contract note 4521 charges", false, false, false)]
    [InlineData("DEBIT", "Charges", false, false, false)]
    [InlineData("DEBIT", "Settlement of unrelated things", false, false, false)] // "stt" is not found inside "settlement"
    [InlineData("", "", false, false, false)]
    public void The_phrases_of_the_table_say_what_the_three_rules_read(string description, string narration, bool charge, bool depository, bool accountLevel)
    {
        (LedgerClassifier.Classify(description, narration, -10m) == LedgerClass.Charges).ShouldBe(charge);
        DepositoryCharges.Is(description, narration).ShouldBe(depository);
        LedgerCosts.IsAccountLevel(description, narration).ShouldBe(accountLevel);
    }

    [Fact]
    public void The_phrases_removed_as_dead_find_the_same_texts_as_before()
    {
        // "dp charges" and "dpc interest" were listed beside "dp charge" and "dpc int", which find them.
        (DepositoryCharges.Is(null, "DP charges"), LedgerCosts.IsAccountLevel(null, "DP charges"), LedgerClassifier.Classify(null, "DP charges", -1m)).ShouldBe((true, true, LedgerClass.Charges));
        (LedgerCosts.IsAccountLevel(null, "DPC Interest"), LedgerClassifier.Classify(null, "DPC Interest", -1m)).ShouldBe((true, LedgerClass.Charges));
    }
}
