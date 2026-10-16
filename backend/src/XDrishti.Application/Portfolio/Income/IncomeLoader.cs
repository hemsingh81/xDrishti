using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>Reads what is recorded for a scope and sorts it out per holding and account (one read of the port, no read per holding).</summary>
internal static class IncomeLoader
{
    public static async ValueTask<IncomeBook> LoadAsync(IIncomeQueries queries, IncomeScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queries);
        return IncomeBook.Build(await queries.ReadAsync(scope, cancellationToken));
    }

    /// <summary>
    /// The scope of an income read for the accounts the holdings were read for. When <paramref name="complete"/> — the holdings are every holding of those accounts (no bucket, status or text narrowed them) — their keys are handed over so the read does
    /// not read them again; otherwise the read reads the keys of all the accounts' holdings itself, because a trade's charges must be matched against every holding, not against the ones that happen to be listed. With <paramref name="accountLevel"/>
    /// false the caller needs the figures of holdings only, and the ledger is not read.
    /// </summary>
    public static IncomeScope ScopeOf(Guid? accountId, bool totalsOnly, IReadOnlyList<HoldingData> holdings, bool complete, bool accountLevel = true)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        return new IncomeScope(
            accountId,
            totalsOnly,
            complete ? [.. holdings.Select(h => new IncomeHolding(h.Id, h.AccountId, h.Isin, h.TradingSymbol, h.IsOpen))] : null,
            accountLevel);
    }
}
