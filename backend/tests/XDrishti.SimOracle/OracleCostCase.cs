namespace XDrishti.SimOracle;

/// <summary>A generated cost case. The sheet is SYNTHETIC: invented numbers, not a broker's and not the TEST sheet.</summary>
public sealed record OracleCostCase(
    OracleCostCategory Category,
    long Seed,
    OracleChargeSheet SyntheticSheet,
    OracleSizing Sizing,
    OracleCostInput Input)
{
    /// <summary>Costs the case with the reference.</summary>
    public OracleCostResult Run(OracleCostFaults faults = OracleCostFaults.None) =>
        ReferenceCosting.Cost(Input, SyntheticSheet, Sizing, faults);
}
