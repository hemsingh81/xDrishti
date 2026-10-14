using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// Which ledger entries of the class <c>charges</c> are costs of the account as such (ADR 0026), as against costs of trades. The six costs of a trade — brokerage, STT, GST, stamp duty, SEBI turnover fee and exchange transaction charges — are
/// already on the trade rows (<c>Transaction.Charges</c>) and a trade settlement on the ledger can mention them again, so the ledger supplies only what no trade row carries: depository (DP) charges, annual maintenance, delayed-payment interest and penalties, platform and
/// subscription fees, pledge, payout and withdrawal charges, demat fees. Table-driven and conservative: an entry counts only when it names one of the <see cref="AccountLevel"/> phrases and none of the <see cref="TradeRelated"/> ones, so a
/// doubt leaves a cost out (it stays visible in the ledger) rather than counting it twice. The class shown for the entry (<see cref="LedgerClassifier"/>) is not changed. The phrases come from Dhan's public documentation and sample, not from the owner's account, so the table is expected to grow after the first real read. Pure.
/// </summary>
internal static class LedgerCosts
{
    /// <summary>Texts that make an entry a trade's: the costs of a trade (on the trade rows) and settlement or bill narrations that sum trades up. Never counted, even when an account-level phrase is also in the text.</summary>
    private static readonly string[] TradeRelated =
    [
        "brokerage", "stt", "securities transaction tax", "stamp duty", "sebi", "transaction charge", "turnover", "settlement", "bill", "contract note", "net obligation",
    ];

    /// <summary>
    /// Costs of the account itself. GST is not a phrase of its own (it would also be GST on a trade): it counts only as part of one of these ("DP charges incl. GST"). "Call and trade" is the fee of an order placed by phone, which is none of the six
    /// costs Dhan reports on a trade row.
    /// </summary>
    private static readonly string[] AccountLevel =
    [
        "dp charge", "dp charges", "depository charge", "demat charge", "amc", "annual maintenance", "account opening",
        "delayed payment", "dpc interest", "dpc int", "late payment", "penalty",
        "platform fee", "subscription fee", "pledge charge", "unpledge charge", "payout charge", "withdrawal charge",
        "call and trade", "call & trade",
    ];

    public static bool IsAccountLevel(string? description, string? narration)
    {
        var text = LedgerText.Of(description, narration);
        return !TradeRelated.Any(p => LedgerText.Has(text, p)) && AccountLevel.Any(p => LedgerText.Has(text, p));
    }
}
