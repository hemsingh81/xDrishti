namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.sim_trades</c> (spec P3-10, D3): the four key columns as properties; the mapped columns of the trade (<see cref="TradeColumns"/>) are shadow properties of the model, declared from that one list so that the model, the
/// copy and the reads cannot drift. It exists for the model and the migration; trades are written by the binary copy and read by a plain reader, never through the change tracker.
/// </summary>
internal sealed class SimTradeRow
{
    public long RunId { get; set; }

    public bool OpenAtEnd { get; set; }

    public int Ordinal { get; set; }

    public string TradeKey { get; set; } = string.Empty;
}
