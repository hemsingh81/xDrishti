namespace XDrishti.SimOracle;

/// <summary>One trade to be sized and charged: the exact and filled prices, the dates and R as doubles.</summary>
public sealed record OracleCostInput(
    OracleSide Side,
    decimal EntryExact,
    decimal EntryFilled,
    decimal StopPrice,
    decimal ExitFilled,
    double RExact,
    double RFilled,
    DateOnly EntryDate,
    DateOnly ExitDate,
    OracleCostSegment Segment,
    decimal Capital,
    decimal MaxCostR);
