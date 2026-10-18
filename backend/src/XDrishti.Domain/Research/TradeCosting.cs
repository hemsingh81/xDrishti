namespace XDrishti.Domain.Research;

/// <summary>
/// What a simulated trade cost (ADR 0042, D13 and D14): the quantity it was sized to, the risk taken, the turnover of each order, the eight lines of each of the two orders (so that the 16 lines of a trade are visible and the charge can be
/// recomputed by hand to the paisa), their sum, the charge in R and R after the sheet's charges. All or nothing: a trade has this exactly when its <see cref="SimTrade.Costs"/> is <see cref="CostBasis.Charged"/>.
/// </summary>
/// <param name="Segment">Which rates the legs paid.</param>
/// <param name="SheetId">The id of the charge sheet.</param>
/// <param name="Quantity">Whole shares, at least 1.</param>
/// <param name="RiskTaken"><c>Quantity × |entry − stop|</c> in rupees: 1R of the trade.</param>
/// <param name="EntryTurnover"><c>Quantity ×</c> the filled entry price.</param>
/// <param name="ExitTurnover"><c>Quantity ×</c> the filled exit price.</param>
/// <param name="EntryLeg">The charge of the entry order, on the schedule in force on the trade date.</param>
/// <param name="ExitLeg">The charge of the exit order, on the schedule in force on the date of the exit minute.</param>
/// <param name="TotalCost">The sum of the 16 lines, a multiple of 0.01.</param>
/// <param name="CostR"><c>TotalCost / RiskTaken</c>, computed in decimal and converted once.</param>
/// <param name="RNet"><c>RFilled − CostR</c>: R after the sheet's charges, from the filled prices (slippage is already in it and is never charged twice).</param>
public sealed record TradeCosting(
    CostSegment Segment,
    string SheetId,
    long Quantity,
    decimal RiskTaken,
    decimal EntryTurnover,
    decimal ExitTurnover,
    LegCharges EntryLeg,
    LegCharges ExitLeg,
    decimal TotalCost,
    double CostR,
    double RNet);
