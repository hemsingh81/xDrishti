namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.sim_trade_legs</c> (spec P4-05, D9): one leg of a stored trade, in the order the trade lists them. It exists for the model and the migration; the legs are written by the binary copy and read by a plain reader, never
/// through the change tracker. The columns are the properties of <see cref="XDrishti.Domain.Research.Leg"/>, which a test compares with them.
/// </summary>
internal sealed class SimTradeLegRow
{
    public long RunId { get; set; }

    public bool OpenAtEnd { get; set; }

    public int Ordinal { get; set; }

    public int LegOrdinal { get; set; }

    public long EpochSeconds { get; set; }

    public double Price { get; set; }

    public double FilledPrice { get; set; }

    public decimal Share { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}
