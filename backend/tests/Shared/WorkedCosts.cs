using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The hand-calculated figures of P3-03c (spec, <i>Worked examples</i>): every line of every leg of W1 to W8 and L1, written as data from the spec before the code existed and re-checked on 2026-10-19 with an independent tool (Python 3
/// <c>decimal</c>, half-up on positive amounts, GST on the rounded brokerage, exchange and SEBI lines of the leg). The tests assert the code against these literals; the code never produces them. TEST sheet, standard profile, capital 10,00,000,
/// <c>max_cost_R</c> 0.10 unless a row says otherwise.
/// </summary>
internal static class WorkedCosts
{
    /// <summary>The eight lines of a leg and its total, as the spec prints them.</summary>
    public sealed record HandLeg(decimal Brokerage, decimal Stt, decimal Exchange, decimal Sebi, decimal Stamp, decimal Other, decimal Dp, decimal Gst, decimal Total)
    {
        public LegCharges Charges => new(Brokerage, Stt, Exchange, Sebi, Stamp, Other, Dp, Gst);
    }

    /// <summary>One worked trade: the quantity, the risk taken, the two turnovers, the two legs, the total, the charge in R and net R.</summary>
    public sealed record HandTrade(string Name, long Quantity, decimal RiskTaken, decimal EntryTurnover, decimal ExitTurnover, HandLeg Entry, HandLeg Exit, decimal Total, decimal CostR, double RFilled)
    {
        public double RNet => RFilled - (double)CostR;
    }

    public static readonly HandLeg W1Entry = new(20.00m, 0.00m, 12.51m, 1.25m, 50.05m, 0m, 0m, 6.08m, 89.89m);

    public static readonly HandLeg W1Exit = new(20.00m, 102.00m, 12.75m, 1.28m, 0m, 0m, 0m, 6.13m, 142.16m);

    /// <summary>W1: BUY, H1 of P3-03b, entry 100.00 filled 100.10, exit 102.00; quantity 2,500 (the cap binds).</summary>
    public static readonly HandTrade W1 = new("W1", 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry, W1Exit, 232.05m, 0.09282m, 1.9);

    public static readonly HandLeg W2Entry = new(20.00m, 99.98m, 12.50m, 1.25m, 0m, 0m, 0m, 6.08m, 139.81m);

    public static readonly HandLeg W2Exit = new(20.00m, 0m, 12.22m, 1.22m, 48.89m, 0m, 0m, 6.02m, 88.35m);

    /// <summary>W2: SELL, H13 of P3-03b, entry at the limit 199.80, exit 195.40; quantity 1,251; the STT is on the entry and the stamp duty on the exit.</summary>
    public static readonly HandTrade W2 = new("W2", 1251, 2752.20m, 249949.80m, 244445.40m, W2Entry, W2Exit, 228.16m, 0.08290095196570017m, 2.0);

    public static readonly HandLeg W3Exit = new(20.00m, 100.40m, 12.55m, 1.26m, 0m, 0m, 0m, 6.09m, 140.30m);

    /// <summary>W3: W1 with the stop at 99.80 (risk 0.20): quantity 2,500, risk taken 500.00, charge in R 0.46038: skipped by cost.</summary>
    public static readonly HandTrade W3 = new("W3", 2500, 500.00m, 250250.00m, 251000.00m, W1Entry, W3Exit, 230.19m, 0.46038m, 1.5);

    public static readonly HandLeg W5Exit = new(20.00m, 89.25m, 12.75m, 1.28m, 0m, 0m, 0m, 6.13m, 129.41m);

    /// <summary>W5: W1 dated 2026-03-03 (schedule B): the exit STT is 89.25.</summary>
    public static readonly HandTrade W5 = new("W5", 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry, W5Exit, 219.30m, 0.08772m, 1.9);

    public static readonly HandLeg W6Entry = new(7.51m, 0m, 1.25m, 0.13m, 5.01m, 0m, 0m, 1.60m, 15.50m);

    public static readonly HandLeg W6Exit = new(7.65m, 10.20m, 1.28m, 0.13m, 0m, 0m, 0m, 1.63m, 20.89m);

    /// <summary>W6: W1 with capital 1,00,000: quantity 250, the percent branch of the brokerage, an exact half in the stamp duty and in the exchange charge.</summary>
    public static readonly HandTrade W6 = new("W6", 250, 250.00m, 25025.00m, 25500.00m, W6Entry, W6Exit, 36.39m, 0.14556m, 1.9);

    public static readonly HandLeg W7Entry = new(20.00m, 0m, 10.05m, 1.01m, 40.20m, 0m, 0m, 5.59m, 76.85m);

    public static readonly HandLeg W7ExitA = new(20.00m, 84.40m, 10.55m, 1.06m, 0m, 0m, 0m, 5.69m, 121.70m);

    public static readonly HandLeg W7ExitB = new(20.00m, 73.85m, 10.55m, 1.06m, 0m, 0m, 0m, 5.69m, 111.15m);

    /// <summary>W7 on schedule A (dated 2026-03-02): the F1 day, BUY, the risk binds, quantity 2,000.</summary>
    public static readonly HandTrade W7A = new("W7A", 2000, 5000.00m, 201000.00m, 211000.00m, W7Entry, W7ExitA, 198.55m, 0.03971m, 2.0);

    /// <summary>W7 on schedule B (dated 2026-03-03, as in the run-record golden file): the exit STT is 73.85.</summary>
    public static readonly HandTrade W7B = new("W7B", 2000, 5000.00m, 201000.00m, 211000.00m, W7Entry, W7ExitB, 188.00m, 0.0376m, 2.0);

    public static readonly HandLeg W8Exit = new(20.00m, 78.40m, 9.80m, 0.98m, 0m, 0m, 0m, 5.54m, 114.72m);

    /// <summary>W8: the F2 day, BUY, stopped out at 98.00 on schedule A: the net R is below the filled R by the charge.</summary>
    public static readonly HandTrade W8 = new("W8", 2000, 5000.00m, 201000.00m, 196000.00m, W7Entry, W8Exit, 191.57m, 0.038314m, -1.0);

    /// <summary>L1: 100 shares, a delivery buy at 1,000.00 on 2026-03-02 (schedule A).</summary>
    public static readonly HandLeg L1Buy = new(0m, 80.00m, 4.00m, 0.50m, 30.00m, 0m, 0m, 0.81m, 115.31m);

    /// <summary>L1: a delivery sell at 1,050.00 on 2026-03-04 (schedule B).</summary>
    public static readonly HandLeg L1Sell = new(0m, 94.50m, 4.20m, 0.53m, 0m, 0m, 10.00m, 0.85m, 110.08m);

    /// <summary>L1: the same sell charged with schedule A (the entry's): 10.50 less in STT.</summary>
    public static readonly HandLeg L1SellOnA = new(0m, 84.00m, 4.20m, 0.53m, 0m, 0m, 10.00m, 0.85m, 99.58m);

    /// <summary>One leg of a worked example: the segment, the side of the order, the turnover, the date and the printed lines.</summary>
    public sealed record LegCase(string Name, CostSegment Segment, Side Side, decimal Turnover, DateOnly Date, HandLeg Expected)
    {
        public override string ToString() => Name;
    }

    private static readonly DateOnly Mar2 = new(2026, 3, 2);

    private static readonly DateOnly Mar3 = new(2026, 3, 3);

    /// <summary>Every leg of W1, W2, W3, W5, W6, W7 (both schedules), W8 and L1, with the lines the spec prints.</summary>
    public static IReadOnlyList<LegCase> Legs { get; } =
    [
        new("W1 entry", CostSegment.IntradayEquity, Side.Buy, 250250.00m, Mar2, W1Entry),
        new("W1 exit", CostSegment.IntradayEquity, Side.Sell, 255000.00m, Mar2, W1Exit),
        new("W2 entry (a sell)", CostSegment.IntradayEquity, Side.Sell, 249949.80m, Mar2, W2Entry),
        new("W2 exit (a buy)", CostSegment.IntradayEquity, Side.Buy, 244445.40m, Mar2, W2Exit),
        new("W3 exit", CostSegment.IntradayEquity, Side.Sell, 251000.00m, Mar2, W3Exit),
        new("W5 exit", CostSegment.IntradayEquity, Side.Sell, 255000.00m, Mar3, W5Exit),
        new("W6 entry", CostSegment.IntradayEquity, Side.Buy, 25025.00m, Mar2, W6Entry),
        new("W6 exit", CostSegment.IntradayEquity, Side.Sell, 25500.00m, Mar2, W6Exit),
        new("W7 entry", CostSegment.IntradayEquity, Side.Buy, 201000.00m, Mar2, W7Entry),
        new("W7 exit on A", CostSegment.IntradayEquity, Side.Sell, 211000.00m, Mar2, W7ExitA),
        new("W7 exit on B", CostSegment.IntradayEquity, Side.Sell, 211000.00m, Mar3, W7ExitB),
        new("W8 exit", CostSegment.IntradayEquity, Side.Sell, 196000.00m, Mar2, W8Exit),
        new("L1 buy", CostSegment.Delivery, Side.Buy, 100000.00m, Mar2, L1Buy),
        new("L1 sell (schedule B)", CostSegment.Delivery, Side.Sell, 105000.00m, new DateOnly(2026, 3, 4), L1Sell),
        new("L1 sell charged with schedule A", CostSegment.Delivery, Side.Sell, 105000.00m, Mar2, L1SellOnA),
    ];
}
