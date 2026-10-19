namespace XDrishti.SimOracle;

/// <summary>The outcome of costing one trade. Legs and totals are null after a quantity skip; RNet is null after any skip.</summary>
public sealed record OracleCostResult(
    OracleCostSkip Skip,
    long Quantity,
    decimal RiskAmount,
    decimal RiskTaken,
    decimal EntryTurnover,
    decimal ExitTurnover,
    OracleLegLines? EntryLeg,
    OracleLegLines? ExitLeg,
    decimal TotalCost,
    decimal CostR,
    double CostRDouble,
    double? RNet,
    IReadOnlySet<string> Trace);
