namespace XDrishti.Infrastructure.Research;

/// <summary>
/// A row of <c>research.sim_trade_leg_charges</c> (spec P4-05, D9): the charges of one fill of a costed trade that has legs, in the order the costing lists them (the fills of one minute are merged, so there may be fewer rows than legs). The
/// columns are the properties of <see cref="XDrishti.Domain.Research.LegCharge"/> and <see cref="XDrishti.Domain.Research.LegCharges"/>, which a test compares with them.
/// </summary>
internal sealed class SimTradeLegChargeRow
{
    public long RunId { get; set; }

    public bool OpenAtEnd { get; set; }

    public int Ordinal { get; set; }

    public int ChargeOrdinal { get; set; }

    public long EpochSeconds { get; set; }

    public long Quantity { get; set; }

    public decimal Turnover { get; set; }

    public decimal Brokerage { get; set; }

    public decimal Stt { get; set; }

    public decimal ExchangeTxn { get; set; }

    public decimal SebiFee { get; set; }

    public decimal StampDuty { get; set; }

    public decimal OtherCharges { get; set; }

    public decimal DpCharge { get; set; }

    public decimal Gst { get; set; }
}
