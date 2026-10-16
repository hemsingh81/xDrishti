using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Whether a ledger entry of Dhan names a depository (DP) charge, from its voucher description and narration (tolerant: any of the <see cref="LedgerText.Kinds.Depository"/> phrases of the one table <see cref="LedgerText.Phrases"/>, at the start of a word, in either text). Shared by the store, the pipeline and the test doubles. Pure.</summary>
public static class DepositoryCharges
{
    public static bool Is(string? description, string? narration) => LedgerText.Any(LedgerText.Of(description, narration), LedgerText.Kinds.Depository);
}
