namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>The eight printed lines of one leg and its total, as the spec table prints them.</summary>
internal sealed record ExpectedLeg(
    decimal Brokerage,
    decimal Stt,
    decimal Exchange,
    decimal Sebi,
    decimal Stamp,
    decimal Other,
    decimal Dp,
    decimal Gst,
    decimal Total);

/// <summary>One worked example of P3-03c, as data: the trade, the sizing and the numbers printed in the table.</summary>
internal sealed record CostExample(
    string Id,
    OracleCostInput Input,
    OracleSizing Sizing,
    OracleCostSkip ExpectedSkip,
    long ExpectedQuantity,
    decimal? RiskTaken,
    decimal? EntryTurnover,
    decimal? ExitTurnover,
    ExpectedLeg? Entry,
    ExpectedLeg? Exit,
    decimal? TotalCost,
    decimal? CostR,
    decimal CostRTolerance,
    double? RNet,
    double RNetTolerance);
