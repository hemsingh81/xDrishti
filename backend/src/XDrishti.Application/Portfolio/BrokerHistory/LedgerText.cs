namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Phrase matching on the free text of a ledger entry (Dhan's voucher description and narration), and the <b>one table</b> of the phrases about charges (<see cref="Phrases"/>), read by the classifier (the class <c>charges</c>, <see cref="LedgerClassifier"/>), by the rule of
/// the account-level costs (<see cref="Income.LedgerCosts"/>) and by the depository-charge test that the supersession of imported sheet lines uses (<see cref="Abstractions.Portfolio.DepositoryCharges"/>), so the three can never disagree about a phrase (ADR 0030).
/// </summary>
internal static class LedgerText
{
    /// <summary>What a phrase of <see cref="Phrases"/> says about an entry whose text has it.</summary>
    [Flags]
    public enum Kinds
    {
        None = 0,

        /// <summary>The entry is a charge (class <c>charges</c>, when the amount is a debit).</summary>
        Charge = 1,

        /// <summary>The charge is a depository (DP) charge: Dhan's own DP charge replaces the same day's imported sheet line.</summary>
        Depository = 2,

        /// <summary>The charge is a cost of the account as such, which no trade row carries (ADR 0026).</summary>
        AccountLevel = 4,

        /// <summary>The entry is a trade's: the costs of a trade (on the trade rows) and settlement or bill narrations that sum trades up. Never counted as a cost of the account, even beside an account-level phrase.</summary>
        TradeRelated = 8,
    }

    /// <summary>A phrase, in lower case, and what it says.</summary>
    public sealed record Phrase(string Text, Kinds Kinds);

    /// <summary>
    /// The phrases, from Dhan's public documentation and its published ledger sample, not from the owner's account, so the table is expected to grow after the first real read. A phrase is listed once, with every kind it has; a phrase that another phrase of the same kind
    /// already finds (for example "dp charges" beside "dp charge": a longer phrase may continue a word) is not listed. <c>depository</c> alone only qualifies an entry that another phrase has made a charge; "gst" is no account-level phrase of its own (it would also be the GST on
    /// a trade) and counts only as part of one ("DP charges incl. GST"); "call and trade" is the fee of an order placed by phone, which is none of the six costs Dhan reports on a trade row.
    /// </summary>
    public static IReadOnlyList<Phrase> Phrases { get; } =
    [
        // The six costs of a trade: on the trade rows already, and a trade settlement on the ledger can mention them again.
        new("brokerage", Kinds.Charge | Kinds.TradeRelated),
        new("stt", Kinds.Charge | Kinds.TradeRelated),
        new("securities transaction tax", Kinds.Charge | Kinds.TradeRelated),
        new("stamp duty", Kinds.Charge | Kinds.TradeRelated),
        new("transaction charge", Kinds.Charge | Kinds.TradeRelated),
        new("turnover charge", Kinds.Charge),
        new("sebi turnover", Kinds.Charge),
        new("sebi", Kinds.TradeRelated),
        new("turnover", Kinds.TradeRelated),
        new("gst", Kinds.Charge),
        new("igst", Kinds.Charge),
        new("cgst", Kinds.Charge),
        new("sgst", Kinds.Charge),

        // Settlement and bill narrations that sum trades up.
        new("settlement", Kinds.TradeRelated),
        new("bill", Kinds.TradeRelated),
        new("contract note", Kinds.TradeRelated),
        new("net obligation", Kinds.TradeRelated),

        // Costs of the account itself: depository (DP) charges, annual maintenance, delayed-payment interest and penalties, platform and subscription fees, pledge, payout and withdrawal charges, demat fees.
        new("dp charge", Kinds.Charge | Kinds.Depository | Kinds.AccountLevel),
        new("demat charge", Kinds.Charge | Kinds.Depository | Kinds.AccountLevel),
        new("depository charge", Kinds.Charge | Kinds.AccountLevel),
        new("dp fee", Kinds.Depository),
        new("demat fee", Kinds.Depository),
        new("depository", Kinds.Depository),
        new("amc", Kinds.Charge | Kinds.AccountLevel),
        new("annual maintenance", Kinds.Charge | Kinds.AccountLevel),
        new("account opening", Kinds.Charge | Kinds.AccountLevel),
        new("delayed payment", Kinds.Charge | Kinds.AccountLevel),
        new("dpc int", Kinds.Charge | Kinds.AccountLevel),
        new("late payment", Kinds.Charge | Kinds.AccountLevel),
        new("penalty", Kinds.Charge | Kinds.AccountLevel),
        new("platform fee", Kinds.Charge | Kinds.AccountLevel),
        new("subscription fee", Kinds.AccountLevel),
        new("pledge charge", Kinds.Charge | Kinds.AccountLevel),
        new("unpledge charge", Kinds.Charge | Kinds.AccountLevel),
        new("payout charge", Kinds.Charge | Kinds.AccountLevel),
        new("withdrawal charge", Kinds.Charge | Kinds.AccountLevel),
        new("call and trade", Kinds.Charge | Kinds.AccountLevel),
        new("call & trade", Kinds.Charge | Kinds.AccountLevel),
    ];

    /// <summary>The narration and the description as one lower-case text (either may be missing).</summary>
    public static string Of(string? description, string? narration) =>
        ((narration ?? string.Empty) + " | " + (description ?? string.Empty)).ToLowerInvariant();

    /// <summary>Whether the text (from <see cref="Of"/>) has a phrase of the table that has <paramref name="kind"/>.</summary>
    public static bool Any(string text, Kinds kind) => Phrases.Any(p => p.Kinds.HasFlag(kind) && Has(text, p.Text));

    /// <summary>
    /// Whether the text has the phrase at the start of a word (so "stt" is not found in "settlement"); a short phrase (four letters or fewer: "upi", "gst", "amc") must also end a word, a longer one may continue ("withdraw" finds "withdrawal").
    /// </summary>
    public static bool Has(string text, string phrase)
    {
        var from = 0;
        while (text.IndexOf(phrase, from, StringComparison.Ordinal) is var at and >= 0)
        {
            var end = at + phrase.Length;
            var startsWord = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var endsWord = phrase.Length > 4 || end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }
}
