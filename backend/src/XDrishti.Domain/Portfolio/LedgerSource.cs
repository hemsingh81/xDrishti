namespace XDrishti.Domain.Portfolio;

/// <summary>Where a ledger entry came from: Dhan's own ledger (ADR 0025) or a downloaded report the owner imported, such as the depository-charges sheet (ADR 0029). Stored by name (lower case) — never rename a value.</summary>
public enum LedgerSource
{
    Broker,
    Import,
}
