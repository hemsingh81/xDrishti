using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>Which ledger entries can be suggested dividends (ADR 0026). Pure.</summary>
internal static class DividendSuggestions
{
    /// <summary>True for a ledger entry of the account with class dividend and money in; the owner's earlier decision is checked apart.</summary>
    public static bool IsOpenCandidate(LedgerEntry entry, Guid accountId) =>
        entry.AccountId == accountId && entry.EntryClass == LedgerClass.Dividend && entry.Amount > 0m;
}
