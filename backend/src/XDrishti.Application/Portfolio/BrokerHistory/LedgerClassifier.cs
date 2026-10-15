using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// Decides the class of a ledger entry from Dhan's voucher description and narration (ADR 0025). Table-driven: the first rule (in the order of <see cref="Rules"/>) whose phrase appears in the lower-cased texts and whose sign fits the amount
/// wins; anything else is <see cref="LedgerClass.Other"/> and keeps its raw text. The phrases of the dividend and funds rules come from Dhan's public documentation and its published ledger sample (voucher description <c>PAYBNK</c>, narration <c>FUNDS WITHDRAWAL</c>; <c>DPC Interest</c> is the delayed payment charge), not from the owner's account, so the table is expected to grow after the first real read. The phrases of the class <c>charges</c> are read from the one table of <see cref="LedgerText.Phrases"/>.
/// A rule never contradicts the sign: money in is positive and money out negative (a "withdrawal" that is a credit is a reversal, hence <c>other</c>). Pure.
/// </summary>
internal static class LedgerClassifier
{
    private enum Sign
    {
        Any,
        Credit,
        Debit,
    }

    private sealed record Rule(LedgerClass Class, Sign Sign, Func<string, bool> Matches)
    {
        public Rule(LedgerClass @class, Sign sign, string[] phrases)
            : this(@class, sign, text => phrases.Any(p => LedgerText.Has(text, p)))
        {
        }
    }

    /// <summary>The rules, in the order they are tried: dividends first (a narration like "dividend credit" is never funds), charges before funds (a "payout charge" is a charge).</summary>
    private static readonly Rule[] Rules =
    [
        new(LedgerClass.Dividend, Sign.Credit, ["dividend", "div payout", "div. payout", "divpayout", "interim div", "final div"]),
        new(LedgerClass.Charges, Sign.Debit, text => LedgerText.Any(text, LedgerText.Kinds.Charge)), // the one table of the charge phrases (LedgerText.Phrases)
        new(LedgerClass.FundsOut, Sign.Debit, ["withdraw", "paybnk", "payout", "pay out", "pay-out", "fund transfer to bank", "refund to bank", "transfer to bank", "bank transfer out"]),
        new(LedgerClass.FundsIn, Sign.Credit, ["deposit", "pay in", "pay-in", "payin", "funds added", "fund added", "add fund", "added funds", "upi", "net banking", "netbanking", "neft", "imps", "rtgs", "bank transfer in", "transfer from bank", "fund transfer"]),
    ];

    public static LedgerClass Classify(string? description, string? narration, decimal amount)
    {
        var text = LedgerText.Of(description, narration);
        foreach (var rule in Rules)
        {
            var fits = rule.Sign switch
            {
                Sign.Credit => amount > 0m,
                Sign.Debit => amount < 0m,
                _ => true,
            };
            if (fits && rule.Matches(text))
            {
                return rule.Class;
            }
        }

        return LedgerClass.Other;
    }
}
