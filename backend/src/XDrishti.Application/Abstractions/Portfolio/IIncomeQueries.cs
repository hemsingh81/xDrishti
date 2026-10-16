namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Read side of the income figures (ADR 0026): what dividends and costs are recorded for the accounts of a scope.</summary>
public interface IIncomeQueries
{
    /// <summary>
    /// Everything the income figures of a scope are made of, in a few set-based reads (no read per holding): the accounts of the scope, their holdings' keys, the dividends that are not voided, the charges of the trades that are not voided
    /// (per account, security and day), the ledger entries of class <c>charges</c> (per account, day and text) and the first date of each account's ledger.
    /// </summary>
    ValueTask<IncomeData> ReadAsync(IncomeScope scope, CancellationToken cancellationToken);
}
