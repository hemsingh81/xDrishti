namespace XDrishti.Application.Reporting;

/// <summary>
/// "What works, in which basket, on which timeframe, with which exit": the cells with enough sample ranked by the credible lower bound of expectancy, the rest after them with the reason, how many cells were looked at, and when no cell qualifies the answer
/// <c>nothing_qualifies</c> with the counts. An answer, not an error.
/// </summary>
/// <param name="Kind">ranked or nothing_qualifies.</param>
/// <param name="Dimensions">The dimensions a cell is made of.</param>
/// <param name="Ranked">Cells with enough sample whose lower bound could be formed, best first.</param>
/// <param name="NotRanked">Every other cell, most trades first.</param>
/// <param name="CellsLookedAt">All cells.</param>
/// <param name="CellsComparable">Cells that compete: every coordinate recorded and the base minimum of trades.</param>
/// <param name="MinTrades">The base minimum sample.</param>
/// <param name="RequiredTrades">The sample a cell needs to be ranked, raised with the number of cells compared.</param>
/// <param name="LargestN">The most trades in any cell.</param>
/// <param name="PositiveLowerBound">Ranked cells with a positive lower bound.</param>
/// <param name="ExpectedChancePositives">About how many positive lower bounds chance alone gives among the ranked cells.</param>
/// <param name="Headline">The answer in one sentence.</param>
/// <param name="SelectionNote">The selection-effect sentence that always accompanies a ranking.</param>
/// <param name="UnavailableDimensions">Dimensions left out of the cells because nothing recorded them, with the reason.</param>
/// <param name="NonAdditive">A trade is in more than one cell.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record WhatWorksView(
    string Kind,
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<RankedCellView> Ranked,
    IReadOnlyList<UnrankedCellView> NotRanked,
    int CellsLookedAt,
    int CellsComparable,
    int MinTrades,
    int RequiredTrades,
    int LargestN,
    int PositiveLowerBound,
    double ExpectedChancePositives,
    string Headline,
    string SelectionNote,
    IReadOnlyList<string> UnavailableDimensions,
    bool NonAdditive,
    ReportEvidenceView Evidence);
