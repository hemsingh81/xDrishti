namespace XDrishti.Domain.Research;

/// <summary>
/// The eight charge lines of one order, in rupees, each rounded to the paisa once (ADR 0042, D6 and D8). Every line is at least 0. GST is computed from the already rounded brokerage, exchange and SEBI lines of the same leg.
/// </summary>
/// <param name="Brokerage">The brokerage of the order.</param>
/// <param name="Stt">Securities transaction tax.</param>
/// <param name="ExchangeTxn">The exchange transaction charge.</param>
/// <param name="SebiFee">The SEBI fee.</param>
/// <param name="StampDuty">Stamp duty (buy orders only).</param>
/// <param name="OtherCharges">The "other" column of a bill, a share of turnover.</param>
/// <param name="DpCharge">The depository charge (delivery sell orders only).</param>
/// <param name="Gst">GST on the brokerage, exchange and SEBI lines.</param>
public sealed record LegCharges(decimal Brokerage, decimal Stt, decimal ExchangeTxn, decimal SebiFee, decimal StampDuty, decimal OtherCharges, decimal DpCharge, decimal Gst)
{
    /// <summary>All zeros.</summary>
    public static LegCharges Zero { get; } = new(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>The sum of the eight lines (a multiple of 0.01).</summary>
    public decimal Total => Brokerage + Stt + ExchangeTxn + SebiFee + StampDuty + OtherCharges + DpCharge + Gst;
}
