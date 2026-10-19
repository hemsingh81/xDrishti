namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>W1 to W8, W1b and L1 of P3-03c encoded as data; every expected number is copied from the spec tables.</summary>
internal static class CostExamples
{
    private static readonly DateOnly D2 = new(2026, 3, 2);
    private static readonly DateOnly D3 = new(2026, 3, 3);
    private static readonly DateOnly D4 = new(2026, 3, 4);

    private static OracleCostInput Buy(
        decimal exact, decimal filled, decimal stop, decimal exit, double rExact, double rFilled,
        DateOnly entryDate, DateOnly exitDate, decimal capital = 1_000_000m, decimal maxCostR = 0.10m,
        OracleCostSegment segment = OracleCostSegment.IntradayEquity) =>
        new(OracleSide.Buy, exact, filled, stop, exit, rExact, rFilled, entryDate, exitDate, segment, capital, maxCostR);

    private static ExpectedLeg Leg(decimal b, decimal stt, decimal ex, decimal sebi, decimal stamp, decimal other, decimal dp, decimal gst, decimal total) =>
        new(b, stt, ex, sebi, stamp, other, dp, gst, total);

    // Legs the spec prints more than once.
    private static readonly ExpectedLeg W1Entry = Leg(20.00m, 0.00m, 12.51m, 1.25m, 50.05m, 0m, 0m, 6.08m, 89.89m);
    private static readonly ExpectedLeg W7Entry = Leg(20.00m, 0.00m, 10.05m, 1.01m, 40.20m, 0m, 0m, 5.59m, 76.85m);

    private static CostExample Kept(
        string id, OracleCostInput input, long quantity, decimal riskTaken, decimal entryT, decimal exitT,
        ExpectedLeg entry, ExpectedLeg exit, decimal total, decimal costR, decimal costRTol, double rNet, double rNetTol,
        OracleSizing? sizing = null) =>
        new(id, input, sizing ?? OracleSizing.Standard, OracleCostSkip.None, quantity, riskTaken, entryT, exitT, entry, exit, total, costR, costRTol, rNet, rNetTol);

    internal static IReadOnlyList<CostExample> All { get; } = Build();

    private static List<CostExample> Build()
    {
        OracleCostInput w1 = Buy(100.00m, 100.10m, 99.00m, 102.00m, 2.0, 1.9, D2, D2);
        ExpectedLeg w1Exit = Leg(20.00m, 102.00m, 12.75m, 1.28m, 0.00m, 0m, 0m, 6.13m, 142.16m);
        OracleCostInput w3 = Buy(100.00m, 100.10m, 99.80m, 100.40m, 2.0, 1.5, D2, D2);
        OracleCostInput w5 = w1 with { EntryDate = D3, ExitDate = D3 };
        OracleCostInput w6 = w1 with { Capital = 100_000m, MaxCostR = 0.20m };
        OracleCostInput w7 = Buy(100.50m, 100.50m, 98.00m, 105.50m, 2.0, 2.0, D2, D2);
        OracleCostInput w8 = Buy(100.50m, 100.50m, 98.00m, 98.00m, -1.0, -1.0, D2, D2);
        ExpectedLeg w6Entry = Leg(7.51m, 0.00m, 1.25m, 0.13m, 5.01m, 0m, 0m, 1.60m, 15.50m);
        ExpectedLeg w6Exit = Leg(7.65m, 10.20m, 1.28m, 0.13m, 0.00m, 0m, 0m, 1.63m, 20.89m);

        return
        [
            Kept("W1", w1, 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry, w1Exit, 232.05m, 0.09282m, 0m, 1.80718, 1e-12),
            Kept("W1b-equal-kept", w1 with { MaxCostR = 0.09282m }, 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry, w1Exit, 232.05m, 0.09282m, 0m, 1.80718, 1e-12),
            new("W1b-below-skipped", w1 with { MaxCostR = 0.09281m }, OracleSizing.Standard, OracleCostSkip.Cost, 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry, w1Exit, 232.05m, 0.09282m, 0m, null, 0),
            Kept(
                "W2",
                new OracleCostInput(OracleSide.Sell, 199.80m, 199.80m, 202.00m, 195.40m, 2.0, 2.0, D2, D2, OracleCostSegment.IntradayEquity, 1_000_000m, 0.10m),
                1251, 2752.20m, 249949.80m, 244445.40m,
                Leg(20.00m, 99.98m, 12.50m, 1.25m, 0.00m, 0m, 0m, 6.08m, 139.81m),
                Leg(20.00m, 0.00m, 12.22m, 1.22m, 48.89m, 0m, 0m, 6.02m, 88.35m),
                228.16m, 0.0829010m, 1e-7m, 1.9171, 5e-5),
            new("W3", w3, OracleSizing.Standard, OracleCostSkip.Cost, 2500, 500.00m, 250250.00m, 251000.00m, W1Entry,
                Leg(20.00m, 100.40m, 12.55m, 1.26m, 0.00m, 0m, 0m, 6.09m, 140.30m), 230.19m, 0.46038m, 0m, null, 0),
            new("W4", Buy(3000.00m, 3000.00m, 2970.00m, 3060.00m, 2.0, 2.0, D2, D2, capital: 10_000m), OracleSizing.Standard,
                OracleCostSkip.Quantity, 0, null, null, null, null, null, null, null, 0m, null, 0),
            new("W4b-boundary-exactly-one", Buy(2500.00m, 2500.00m, 2470.00m, 2560.00m, 2.0, 2.0, D2, D2, capital: 10_000m, maxCostR: 1000m), OracleSizing.Standard,
                OracleCostSkip.None, 1, 30.00m, 2500.00m, 2560.00m, null, null, null, null, 0m, null, 0),
            new("W4c-just-under-one", Buy(2500.25m, 2500.25m, 2470.25m, 2560.25m, 2.0, 2.0, D2, D2, capital: 10_000m), OracleSizing.Standard,
                OracleCostSkip.Quantity, 0, null, null, null, null, null, null, null, 0m, null, 0),
            Kept("W5", w5, 2500, 2500.00m, 250250.00m, 255000.00m, W1Entry,
                Leg(20.00m, 89.25m, 12.75m, 1.28m, 0.00m, 0m, 0m, 6.13m, 129.41m), 219.30m, 0.08772m, 0m, 1.81228, 1e-12),
            Kept("W6", w6, 250, 250.00m, 25025.00m, 25500.00m, w6Entry, w6Exit, 36.39m, 0.14556m, 0m, 1.75444, 1e-12),
            new("W6-skipped-at-0.10", w6 with { MaxCostR = 0.10m }, OracleSizing.Standard, OracleCostSkip.Cost, 250, 250.00m, 25025.00m, 25500.00m, w6Entry, w6Exit, 36.39m, 0.14556m, 0m, null, 0),
            Kept("W7-A", w7, 2000, 5000.00m, 201000.00m, 211000.00m, W7Entry,
                Leg(20.00m, 84.40m, 10.55m, 1.06m, 0.00m, 0m, 0m, 5.69m, 121.70m), 198.55m, 0.03971m, 0m, 1.96029, 1e-12),
            Kept("W7-B", w7 with { EntryDate = D3, ExitDate = D3 }, 2000, 5000.00m, 201000.00m, 211000.00m, W7Entry,
                Leg(20.00m, 73.85m, 10.55m, 1.06m, 0.00m, 0m, 0m, 5.69m, 111.15m), 188.00m, 0.0376m, 0m, 1.9624, 1e-12),
            Kept("W8", w8, 2000, 5000.00m, 201000.00m, 196000.00m, W7Entry,
                Leg(20.00m, 78.40m, 9.80m, 0.98m, 0.00m, 0m, 0m, 5.54m, 114.72m), 191.57m, 0.038314m, 0m, -1.038314, 1e-12),
            Kept(
                "L1",
                Buy(1000.00m, 1000.00m, 990.00m, 1050.00m, 5.0, 5.0, D2, D4, capital: 400_000m, maxCostR: 1m, segment: OracleCostSegment.Delivery),
                100, 1000.00m, 100000.00m, 105000.00m,
                Leg(0.00m, 80.00m, 4.00m, 0.50m, 30.00m, 0m, 0m, 0.81m, 115.31m),
                Leg(0.00m, 94.50m, 4.20m, 0.53m, 0.00m, 0m, 10.00m, 0.85m, 110.08m),
                225.39m, 0.22539m, 0m, 4.77461, 1e-12),
        ];
    }

    /// <summary>The id table: every example id of the spec and where it is encoded.</summary>
    internal static IReadOnlyDictionary<string, string[]> IdTable { get; } = new Dictionary<string, string[]>
    {
        ["W1"] = ["W1"],
        ["W1b"] = ["W1b-equal-kept", "W1b-below-skipped"],
        ["W2"] = ["W2"],
        ["W3"] = ["W3"],
        ["W4"] = ["W4", "W4b-boundary-exactly-one", "W4c-just-under-one"],
        ["W5"] = ["W5"],
        ["W6"] = ["W6", "W6-skipped-at-0.10"],
        ["W7"] = ["W7-A", "W7-B"],
        ["W8"] = ["W8"],
        ["L1"] = ["L1"],
    };
}
