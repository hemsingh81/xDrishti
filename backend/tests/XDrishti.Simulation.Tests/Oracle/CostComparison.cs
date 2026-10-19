using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>One trade costed on both sides: the oracle's result (null when it refused the date), the two views and every mismatch.</summary>
internal sealed record CostOutcome(OracleCostInput Input, OracleCostResult? Oracle, CostView OracleView, CostView EngineView, IReadOnlyList<Mismatch> Mismatches);

/// <summary>
/// The comparison of one costing (spec P3-08c): the oracle sizes and charges the trade from its own records, the engine does the same through <see cref="CostAdapter"/> and <see cref="TradeCostApplier"/>, and every field of
/// <see cref="CostView"/> is compared under D5. The oracle is always run with <see cref="OracleCostFaults.None"/>: a production comparison never switches a fault on (the architecture test scans the IL for it).
/// </summary>
internal static class CostComparison
{
    internal static CostOutcome Run(OracleCostInput input, OracleChargeSheet oracleSheet, CostSheet engineSheet, OracleSizing sizing, SizingProfile profile, RatioStats stats, string where) =>
        Run(input, CostAdapter.EngineTrade(input), oracleSheet, engineSheet, sizing, profile, stats, where);

    /// <summary>The same with the engine's own gross trade given (the chain: the walker's trade, not one rebuilt from the oracle's input).</summary>
    internal static CostOutcome Run(OracleCostInput input, SimTrade trade, OracleChargeSheet oracleSheet, CostSheet engineSheet, OracleSizing sizing, SizingProfile profile, RatioStats stats, string where)
    {
        OracleCostResult? oracle = null;
        CostView oracleView;
        try
        {
            oracle = ReferenceCosting.Cost(input, oracleSheet, sizing, OracleCostFaults.None);
            oracleView = CostView.Of(oracle, input.Segment, engineSheet.Id);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("costs.no_rates_for_date", StringComparison.Ordinal))
        {
            oracleView = CostView.OfFate("error:costs.no_rates_for_date");
        }

        var engineView = CostView.Of(CostAdapter.Apply(trade, engineSheet, profile, input.Capital, input.MaxCostR, CostAdapter.Segment(input.Segment)));
        var mismatches = CostComparer.Diff(oracleView, engineView, stats, where);
        if (oracle is not null && CostAdapter.Clamped(input.MaxCostR) && oracle.CostR > CostAdapter.EngineMaxCostR)
        {
            // the adapter gave the engine its bound instead of the limit; that is only the same decision while the charge in R is under the bound
            mismatches.Add(new Mismatch("MaxCostR", input.MaxCostR.ToString(System.Globalization.CultureInfo.InvariantCulture), CostAdapter.EngineMaxCostR.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new CostOutcome(input, oracle, oracleView, engineView, mismatches);
    }

    /// <summary>Costs one of the oracle's generated cases on both sides.</summary>
    internal static CostOutcome Run(OracleCostCase source, RatioStats stats, string where) =>
        Run(source.Input, source.SyntheticSheet, CostAdapter.EngineSheet(source.SyntheticSheet), source.Sizing, CostAdapter.Profile(source.Sizing), stats, where);
}
