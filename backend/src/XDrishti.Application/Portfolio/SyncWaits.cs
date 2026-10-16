namespace XDrishti.Application.Portfolio;

/// <summary>
/// The kinds of sync run a write waits for (ADR 0030), by what the run changes: a holdings sync changes the holdings and their lots, a trades run the stored trades and (by its automatic rebuild) the lots, a ledger run the ledger entries. A write waits only for the runs that
/// change what it reads and writes, so a ledger-only run does not block a trade Undo or Rebuild.
/// </summary>
[Flags]
internal enum SyncWaits
{
    /// <summary>Waits for nothing (the demat import only learns names).</summary>
    None = 0,

    /// <summary>A holdings sync of the account.</summary>
    Holdings = 1,

    /// <summary>A broker history run of trades.</summary>
    Trades = 2,

    /// <summary>A broker history run of the ledger.</summary>
    Ledger = 4,

    /// <summary>Trade import (also a Dhan report's bills), Undo of a trade batch and Rebuild of lots: they read and write the holdings, the lots and the trades.</summary>
    TradeWrite = Holdings | Trades,

    /// <summary>Import of a depository (DP) charges sheet: it reads the holdings and the ledger and writes cost entries.</summary>
    CostWrite = Holdings | Ledger,
}
