namespace XDrishti.Domain.Accounts;

/// <summary>Instrument kinds an account may trade for a style (a set: combine with |).</summary>
[Flags]
public enum Instruments
{
    None = 0,

    /// <summary>Equity, intraday (MIS).</summary>
    EquityIntraday = 1,

    /// <summary>Equity, delivery (CNC).</summary>
    EquityDelivery = 2,

    Futures = 4,
}
