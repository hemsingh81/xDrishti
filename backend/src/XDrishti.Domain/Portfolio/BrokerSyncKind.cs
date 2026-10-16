namespace XDrishti.Domain.Portfolio;

/// <summary>What a broker history run reads (ADR 0025): the trade history or the ledger. Stored by name (lower case) — never rename a value.</summary>
public enum BrokerSyncKind
{
    Trades,
    Ledger,
}
