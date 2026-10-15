using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Whether a ledger entry of Dhan names a depository (DP) charge, from its voucher description and narration (tolerant: any of the phrases, at the start of a word, in either text). Shared by the store, the pipeline and the test doubles. Pure.</summary>
public static class DepositoryCharges
{
    private static readonly string[] Phrases = ["dp charge", "dp fee", "depository", "demat charge", "demat fee"];

    public static bool Is(string? description, string? narration)
    {
        var text = LedgerText.Of(description, narration);
        return Phrases.Any(p => LedgerText.Has(text, p));
    }
}
