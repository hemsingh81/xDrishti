namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Dhan's ledger for a period after mapping: the usable <see cref="Rows"/>, how many rows Dhan sent (<see cref="Received"/>) and how many were unusable or without an amount (<see cref="Dropped"/>). <see cref="NoData"/> says the read is empty because Dhan answered its data error DH-907 ("incorrect parameters or no data present") instead of an empty list; the reader decides what that means. Counts only.</summary>
public sealed record BrokerLedgerRead(IReadOnlyList<BrokerLedgerRow> Rows, int Received, int Dropped, bool NoData = false)
{
    public static BrokerLedgerRead Empty { get; } = new([], 0, 0);

    /// <summary>The read of an answer that was Dhan's "no data" code (DH-907).</summary>
    public static BrokerLedgerRead NoDataAnswer { get; } = Empty with { NoData = true };
}
