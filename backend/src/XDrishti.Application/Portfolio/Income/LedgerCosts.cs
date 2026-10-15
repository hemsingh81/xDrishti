using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// Which ledger entries of the class <c>charges</c> are costs of the account as such (ADR 0026), as against costs of trades. The six costs of a trade — brokerage, STT, GST, stamp duty, SEBI turnover fee and exchange transaction charges — are
/// already on the trade rows (<c>Transaction.Charges</c>) and a trade settlement on the ledger can mention them again, so the ledger supplies only what no trade row carries: depository (DP) charges, annual maintenance, delayed-payment interest and penalties, platform and
/// subscription fees, pledge, payout and withdrawal charges, demat fees. Conservative: an entry counts only when it names one of the phrases of the table <see cref="LedgerText.Phrases"/> that are <see cref="LedgerText.Kinds.AccountLevel"/> and none of the
/// <see cref="LedgerText.Kinds.TradeRelated"/> ones, so a doubt leaves a cost out (it stays visible in the ledger) rather than counting it twice. The same table is read by the classifier (<see cref="LedgerClassifier"/>, which gives the class shown for the entry and is not changed by
/// this rule). Pure.
/// </summary>
internal static class LedgerCosts
{
    public static bool IsAccountLevel(string? description, string? narration)
    {
        var text = LedgerText.Of(description, narration);
        return !LedgerText.Any(text, LedgerText.Kinds.TradeRelated) && LedgerText.Any(text, LedgerText.Kinds.AccountLevel);
    }
}
